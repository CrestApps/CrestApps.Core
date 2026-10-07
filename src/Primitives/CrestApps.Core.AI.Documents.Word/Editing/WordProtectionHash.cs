using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CrestApps.Core.AI.Documents.Word.Editing;

/// <summary>
/// Hashes a document protection password the way Word does, so Word accepts the password to stop the
/// protection (ISO/IEC 29500-1 §17.15.1.29, with the Word behaviour described in [MS-OE376] and [MS-OI29500]).
/// </summary>
/// <remarks>
/// The password is first reduced to the legacy 32-bit key of older Word versions, whose bytes in reversed
/// order are written as an uppercase hexadecimal string. That string, in UTF-16LE, is hashed with SHA-512
/// after the salt, and the result is hashed again <c>spinCount</c> times, each time followed by the iteration
/// number as a 4-byte little-endian integer. Word only reads the first 15 characters of the password.
/// </remarks>
internal static class WordProtectionHash
{
    /// <summary>
    /// The number of hashing rounds Word uses.
    /// </summary>
    public const int SpinCount = 100_000;

    /// <summary>
    /// The <c>cryptAlgorithmSid</c> of SHA-512.
    /// </summary>
    public const int Sha512AlgorithmSid = 14;

    /// <summary>
    /// The most characters of a password Word reads.
    /// </summary>
    public const int MaxPasswordLength = 15;

    private static readonly ushort[] _initialCode =
    [
        0xE1F0, 0x1D0F, 0xCC9C, 0x84C0, 0x110C, 0x0E10, 0xF1CE, 0x313E, 0x1872, 0xE139, 0xD40F, 0x84F9, 0x280C, 0xA96A, 0x4EC3,
    ];

    private static readonly ushort[,] _encryptionMatrix =
    {
        { 0xAEFC, 0x4DD9, 0x9BB2, 0x2745, 0x4E8A, 0x9D14, 0x2A09 },
        { 0x7B61, 0xF6C2, 0xFDA5, 0xEB6B, 0xC6F7, 0x9DCF, 0x2BBF },
        { 0x4563, 0x8AC6, 0x05AD, 0x0B5A, 0x16B4, 0x2D68, 0x5AD0 },
        { 0x0375, 0x06EA, 0x0DD4, 0x1BA8, 0x3750, 0x6EA0, 0xDD40 },
        { 0xD849, 0xA0B3, 0x5147, 0xA28E, 0x553D, 0xAA7A, 0x44D5 },
        { 0x6F45, 0xDE8A, 0xAD35, 0x4A4B, 0x9496, 0x390D, 0x721A },
        { 0xEB23, 0xC667, 0x9CEF, 0x29FF, 0x53FE, 0xA7FC, 0x5FD9 },
        { 0x47D3, 0x8FA6, 0x0F6D, 0x1EDA, 0x3DB4, 0x7B68, 0xF6D0 },
        { 0xB861, 0x60E3, 0xC1C6, 0x93AD, 0x377B, 0x6EF6, 0xDDEC },
        { 0x45A0, 0x8B40, 0x06A1, 0x0D42, 0x1A84, 0x3508, 0x6A10 },
        { 0xAA51, 0x4483, 0x8906, 0x022D, 0x045A, 0x08B4, 0x1168 },
        { 0x76B4, 0xED68, 0xCAF1, 0x85C3, 0x1BA7, 0x374E, 0x6E9C },
        { 0x3730, 0x6E60, 0xDCC0, 0xA9A1, 0x4363, 0x86C6, 0x1DAD },
        { 0x3331, 0x6662, 0xCCC4, 0x89A9, 0x0373, 0x06E6, 0x0DCC },
        { 0x1021, 0x2042, 0x4084, 0x8108, 0x1231, 0x2462, 0x48C4 },
    };

    /// <summary>
    /// Computes the legacy key of a password: its high word from the initial code and the encryption matrix,
    /// its low word from the password verifier.
    /// </summary>
    /// <param name="password">The password.</param>
    /// <returns>The key, or 0 for an empty password.</returns>
    public static uint LegacyKey(string password)
    {
        var bytes = SingleBytes(password);

        if (bytes.Length == 0)
        {
            return 0;
        }

        uint high = _initialCode[bytes.Length - 1];

        for (var index = 0; index < bytes.Length; index++)
        {
            // The last character uses the last row of the matrix, the one before it the row before, and so on.
            var row = MaxPasswordLength - bytes.Length + index;

            for (var bit = 0; bit < 7; bit++)
            {
                if ((bytes[index] & (1 << bit)) != 0)
                {
                    high ^= _encryptionMatrix[row, bit];
                }
            }
        }

        uint low = 0;

        for (var index = bytes.Length - 1; index >= 0; index--)
        {
            low = (((low >> 14) & 0x0001) | ((low << 1) & 0x7FFF)) ^ bytes[index];
        }

        low = (((low >> 14) & 0x0001) | ((low << 1) & 0x7FFF)) ^ (uint)bytes.Length ^ 0xCE4B;

        return (high << 16) | low;
    }

    /// <summary>
    /// Hashes a password with a salt.
    /// </summary>
    /// <param name="password">The password.</param>
    /// <param name="salt">The salt.</param>
    /// <param name="spinCount">The number of rounds after the first hash.</param>
    /// <returns>The SHA-512 hash.</returns>
    public static byte[] Hash(string password, byte[] salt, int spinCount = SpinCount)
    {
        ArgumentNullException.ThrowIfNull(salt);

        Span<byte> key = stackalloc byte[4];

        BinaryPrimitives.WriteUInt32LittleEndian(key, LegacyKey(password));

        var hex = new StringBuilder(8);

        foreach (var value in key)
        {
            hex.Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }

        var hash = SHA512.HashData([.. salt, .. Encoding.Unicode.GetBytes(hex.ToString())]);
        var buffer = new byte[hash.Length + 4];

        for (var iteration = 0; iteration < spinCount; iteration++)
        {
            hash.CopyTo(buffer, 0);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(hash.Length), iteration);
            hash = SHA512.HashData(buffer);
        }

        return hash;
    }

    // Truncated to 15 characters, each character's low byte, or its high byte when the low one is zero.
    private static byte[] SingleBytes(string password)
    {
        var text = password ?? string.Empty;

        if (text.Length > MaxPasswordLength)
        {
            text = text[..MaxPasswordLength];
        }

        var bytes = new byte[text.Length];

        for (var index = 0; index < text.Length; index++)
        {
            var low = (byte)(text[index] & 0xFF);

            bytes[index] = low != 0 ? low : (byte)(text[index] >> 8);
        }

        return bytes;
    }
}
