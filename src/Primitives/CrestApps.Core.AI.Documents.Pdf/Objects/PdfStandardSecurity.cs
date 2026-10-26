using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using UglyToad.PdfPig.Tokens;

namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// The key derivations and password checks of the PDF standard security handler, revisions 2 to 6, as
/// ISO 32000-2 section 7.6.4 describes them.
/// </summary>
/// <remarks>
/// PdfPig decrypts a file with either password and does not say which one it took. An edit needs the owner
/// password, so it is checked here against the file's encryption dictionary. <see cref="PdfStandardEncryptor"/>
/// derives the entries it writes with the same steps.
/// </remarks>
#pragma warning disable CA5351 // The standard security handler is built on MD5; the format, not this code, chose it.
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
        var key = OwnerKey(password, revision, length);
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

        var encryptMetadata = encrypt.Data.TryGetValue("EncryptMetadata", out var metadata) && metadata is BooleanToken flag
            ? flag.Data
            : true;

        var fileKey = FileKey(candidate, owner[..Math.Min(owner.Length, 32)], (int)(Integer(encrypt, "P") ?? 0), firstIdentifier, revision, length, encryptMetadata);
        var expected = UserEntry(fileKey, firstIdentifier, revision);

        // From revision 3 on, only the first 16 bytes of the user entry are significant.
        var significant = revision == 2
            ? 32
            : 16;

        return user.Length >= significant && CryptographicOperations.FixedTimeEquals(expected.AsSpan(0, significant), user.AsSpan(0, significant));
    }

    /// <summary>
    /// Derives the key the owner entry is encrypted with (algorithm 3, steps a to d).
    /// </summary>
    /// <param name="password">The owner password.</param>
    /// <param name="revision">The security handler revision, 2 to 4.</param>
    /// <param name="length">The key length, in bytes.</param>
    /// <returns>The key.</returns>
    internal static byte[] OwnerKey(string password, int revision, int length)
    {
        var key = MD5.HashData(Pad(Latin1Password(password ?? string.Empty)));

        if (revision >= 3)
        {
            for (var round = 0; round < 50; round++)
            {
                key = MD5.HashData(key);
            }
        }

        return key[..length];
    }

    /// <summary>
    /// Derives the file key from the user password (algorithm 2).
    /// </summary>
    /// <param name="password">The user password, as bytes; it is padded to 32.</param>
    /// <param name="owner">The owner entry.</param>
    /// <param name="permissions">The permission flags.</param>
    /// <param name="firstIdentifier">The first part of the file identifier.</param>
    /// <param name="revision">The security handler revision, 2 to 4.</param>
    /// <param name="length">The key length, in bytes.</param>
    /// <param name="encryptMetadata">Whether the metadata stream is encrypted.</param>
    /// <returns>The key.</returns>
    internal static byte[] FileKey(byte[] password, byte[] owner, int permissions, byte[] firstIdentifier, int revision, int length, bool encryptMetadata)
    {
        var permissionBytes = new byte[4];
        var input = new List<byte>(128);

        BinaryPrimitives.WriteInt32LittleEndian(permissionBytes, permissions);
        input.AddRange(Pad(password));
        input.AddRange(owner);
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

        return key[..length];
    }

    /// <summary>
    /// Computes the user entry a file key gives (algorithms 4 and 5).
    /// </summary>
    /// <param name="fileKey">The file key.</param>
    /// <param name="firstIdentifier">The first part of the file identifier.</param>
    /// <param name="revision">The security handler revision, 2 to 4.</param>
    /// <returns>The 32-byte entry; from revision 3 on, its last 16 bytes are zeros.</returns>
    internal static byte[] UserEntry(byte[] fileKey, byte[] firstIdentifier, int revision)
    {
        if (revision == 2)
        {
            return Rc4(fileKey, _padding);
        }

        var check = Rc4(fileKey, MD5.HashData([.. _padding, .. firstIdentifier ?? []]));

        for (var round = 1; round <= 19; round++)
        {
            check = Rc4(Xor(fileKey, round), check);
        }

        return [.. check, .. new byte[16]];
    }

    /// <summary>
    /// The hash of revision 6 (algorithm 2.B of ISO 32000-2): rounds of AES and SHA-2 that make guessing slow.
    /// </summary>
    /// <param name="password">The password, as <see cref="Utf8Password"/> encodes it.</param>
    /// <param name="salt">The salt.</param>
    /// <param name="userKey">The 48-byte user entry, for an owner hash; empty for a user hash.</param>
    /// <returns>The 32-byte hash.</returns>
    internal static byte[] HardenedHash(byte[] password, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> userKey)
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

    /// <summary>
    /// Pads or cuts a password to the 32 bytes revisions 2 to 4 work with.
    /// </summary>
    /// <param name="password">The password's bytes.</param>
    /// <returns>The padded password.</returns>
    internal static byte[] Pad(byte[] password)
    {
        var padded = new byte[32];
        var length = Math.Min(password.Length, 32);

        Array.Copy(password, padded, length);
        Array.Copy(_padding, 0, padded, length, 32 - length);

        return padded;
    }

    /// <summary>
    /// Encodes a password for revisions 2 to 4, which take single bytes.
    /// </summary>
    /// <param name="password">The password.</param>
    /// <returns>Its bytes; a character beyond Latin-1 becomes <c>?</c>.</returns>
    internal static byte[] Latin1Password(string password)
    {
        return [.. password.Select(character => character <= 0xFF ? (byte)character : (byte)'?')];
    }

    /// <summary>
    /// Encodes a password for revisions 5 and 6, which take UTF-8 of at most 127 bytes.
    /// </summary>
    /// <param name="password">The password.</param>
    /// <returns>Its bytes.</returns>
    internal static byte[] Utf8Password(string password)
    {
        var bytes = Encoding.UTF8.GetBytes((password ?? string.Empty).Normalize(NormalizationForm.FormKC));

        return bytes.Length > 127
            ? bytes[..127]
            : bytes;
    }

    /// <summary>
    /// Combines every byte of a key with a value, as the rounds of revisions 3 and 4 do.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>The new key.</returns>
    internal static byte[] Xor(byte[] key, int value)
    {
        return [.. key.Select(part => (byte)(part ^ value))];
    }

    /// <summary>
    /// Encrypts or decrypts data with RC4, which the format still uses for its older revisions.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="data">The data.</param>
    /// <returns>The result.</returns>
    internal static byte[] Rc4(byte[] key, ReadOnlySpan<byte> data)
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
