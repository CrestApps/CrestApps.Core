using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using UglyToad.PdfPig.Tokens;

namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// The password checks of the PDF standard security handler, revisions 2 to 6: whether a password is the
/// owner password, the one that allows changes, rather than only the one that opens the file.
/// </summary>
/// <remarks>
/// PdfPig decrypts a file with either password and does not say which one it took. An edit needs the owner
/// password, so it is checked here against the file's encryption dictionary, as ISO 32000-2 section 7.6.4
/// describes.
/// </remarks>
#pragma warning disable CA5351 // The standard security handler is built on MD5; the file, not this code, chose it.
internal static class PdfStandardSecurity
{
    private static readonly byte[] _padding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    ];

    /// <summary>
    /// Returns whether a password is the owner password of an encrypted file.
    /// </summary>
    /// <param name="encrypt">The file's encryption dictionary.</param>
    /// <param name="firstIdentifier">The first part of the file identifier, or <see langword="null"/>.</param>
    /// <param name="password">The password; <see langword="null"/> is the empty password.</param>
    /// <returns><see langword="true"/> when the password is the owner password.</returns>
    public static bool IsOwnerPassword(DictionaryToken encrypt, byte[] firstIdentifier, string password)
    {
        ArgumentNullException.ThrowIfNull(encrypt);

        // Only the standard handler has an owner password this can check; another handler grants what it grants.
        if (Name(encrypt, "Filter") is { } filter && filter != "Standard")
        {
            return true;
        }

        var revision = (int)(Integer(encrypt, "R") ?? 2);
        var owner = Bytes(encrypt, "O");
        var user = Bytes(encrypt, "U");

        if (owner is null || user is null)
        {
            return false;
        }

        password ??= string.Empty;

        if (revision >= 5)
        {
            if (owner.Length < 40 || user.Length < 48)
            {
                return false;
            }

            var secret = Utf8Password(password);
            var hash = revision == 5
                ? SHA256.HashData([.. secret, .. owner.AsSpan(32, 8), .. user.AsSpan(0, 48)])
                : HardenedHash(secret, owner.AsSpan(32, 8), user.AsSpan(0, 48));

            return CryptographicOperations.FixedTimeEquals(hash.AsSpan(0, 32), owner.AsSpan(0, 32));
        }

        var length = revision == 2
            ? 5
            : (int)Math.Clamp((Integer(encrypt, "Length") ?? 40) / 8, 5, 16);

        // The owner password's key decrypts the owner entry back into the user password, which then has to check out.
        var key = MD5.HashData(Pad(Latin1Password(password)));

        if (revision >= 3)
        {
            for (var round = 0; round < 50; round++)
            {
                key = MD5.HashData(key);
            }
        }

        key = key[..length];

        byte[] candidate;

        if (revision == 2)
        {
            candidate = Rc4(key, owner.AsSpan(0, Math.Min(owner.Length, 32)));
        }
        else
        {
            candidate = owner[..Math.Min(owner.Length, 32)];

            for (var round = 19; round >= 0; round--)
            {
                candidate = Rc4(Xor(key, round), candidate);
            }
        }

        return IsUserPassword(encrypt, firstIdentifier, candidate, revision, length, owner, user);
    }

    private static bool IsUserPassword(DictionaryToken encrypt, byte[] firstIdentifier, byte[] padded, int revision, int length, byte[] owner, byte[] user)
    {
        var permissions = Integer(encrypt, "P") ?? 0;
        var encryptMetadata = encrypt.Data.TryGetValue("EncryptMetadata", out var metadata) && metadata is BooleanToken flag
            ? flag.Data
            : true;

        var permissionBytes = new byte[4];
        var input = new List<byte>(128);

        BinaryPrimitives.WriteInt32LittleEndian(permissionBytes, (int)permissions);
        input.AddRange(Pad(padded));
        input.AddRange(owner.AsSpan(0, Math.Min(owner.Length, 32)).ToArray());
        input.AddRange(permissionBytes);
        input.AddRange(firstIdentifier ?? []);

        if (revision >= 4 && !encryptMetadata)
        {
            input.AddRange([0xFF, 0xFF, 0xFF, 0xFF]);
        }

        var key = MD5.HashData([.. input]);

        if (revision >= 3)
        {
            for (var round = 0; round < 50; round++)
            {
                key = MD5.HashData(key.AsSpan(0, length));
            }
        }

        key = key[..length];

        if (revision == 2)
        {
            var expected = Rc4(key, _padding);

            return user.Length >= 32 && CryptographicOperations.FixedTimeEquals(expected, user.AsSpan(0, 32));
        }

        var check = Rc4(key, MD5.HashData([.. _padding, .. firstIdentifier ?? []]));

        for (var round = 1; round <= 19; round++)
        {
            check = Rc4(Xor(key, round), check);
        }

        return user.Length >= 16 && CryptographicOperations.FixedTimeEquals(check.AsSpan(0, 16), user.AsSpan(0, 16));
    }

    /// <summary>
    /// The hash of revision 6 (algorithm 2.B of ISO 32000-2): rounds of AES and SHA-2 that make guessing slow.
    /// </summary>
    private static byte[] HardenedHash(byte[] password, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> userKey)
    {
        var hash = SHA256.HashData([.. password, .. salt, .. userKey]);
        var block = new List<byte>();

        using var aes = Aes.Create();

        for (var round = 0; ; round++)
        {
            block.Clear();

            for (var repeat = 0; repeat < 64; repeat++)
            {
                block.AddRange(password);
                block.AddRange(hash);
                block.AddRange(userKey.ToArray());
            }

            aes.Key = hash[..16];

            var encrypted = aes.EncryptCbc([.. block], hash.AsSpan(16, 16), PaddingMode.None);
            var remainder = 0;

            for (var index = 0; index < 16; index++)
            {
                remainder += encrypted[index];
            }

            hash = (remainder % 3) switch
            {
                0 => SHA256.HashData(encrypted),
                1 => SHA384.HashData(encrypted),
                _ => SHA512.HashData(encrypted),
            };

            if (round >= 63 && encrypted[^1] <= round - 31)
            {
                return hash[..32];
            }
        }
    }

    private static byte[] Pad(byte[] password)
    {
        var padded = new byte[32];
        var length = Math.Min(password.Length, 32);

        Array.Copy(password, padded, length);
        Array.Copy(_padding, 0, padded, length, 32 - length);

        return padded;
    }

    private static byte[] Latin1Password(string password)
    {
        return [.. password.Select(character => character <= 0xFF ? (byte)character : (byte)'?')];
    }

    private static byte[] Utf8Password(string password)
    {
        var bytes = Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormKC));

        return bytes.Length > 127
            ? bytes[..127]
            : bytes;
    }

    private static byte[] Xor(byte[] key, int value)
    {
        return [.. key.Select(part => (byte)(part ^ value))];
    }

    private static byte[] Rc4(byte[] key, ReadOnlySpan<byte> data)
    {
        var state = new byte[256];

        for (var index = 0; index < 256; index++)
        {
            state[index] = (byte)index;
        }

        for (int index = 0, swap = 0; index < 256; index++)
        {
            swap = (swap + state[index] + key[index % key.Length]) & 0xFF;
            (state[index], state[swap]) = (state[swap], state[index]);
        }

        var output = new byte[data.Length];

        for (int index = 0, i = 0, j = 0; index < data.Length; index++)
        {
            i = (i + 1) & 0xFF;
            j = (j + state[i]) & 0xFF;
            (state[i], state[j]) = (state[j], state[i]);
            output[index] = (byte)(data[index] ^ state[(state[i] + state[j]) & 0xFF]);
        }

        return output;
    }

    private static string Name(DictionaryToken dictionary, string key)
    {
        return dictionary.Data.TryGetValue(key, out var value) && value is NameToken name
            ? name.Data
            : null;
    }

    private static long? Integer(DictionaryToken dictionary, string key)
    {
        return dictionary.Data.TryGetValue(key, out var value) && value is NumericToken number
            ? number.Long
            : null;
    }

    private static byte[] Bytes(DictionaryToken dictionary, string key)
    {
        if (!dictionary.Data.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            StringToken text => text.GetBytes(),
            HexToken hex => [.. hex.Bytes],
            _ => null,
        };
    }
}
#pragma warning restore CA5351
