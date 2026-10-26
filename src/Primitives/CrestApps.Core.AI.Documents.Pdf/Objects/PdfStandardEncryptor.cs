using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// Encrypts a file with the standard security handler as <see cref="PdfObjectWriter"/> writes it: a password
/// that opens it, an owner password that lifts its permissions, and the permissions themselves.
/// </summary>
#pragma warning disable CA5351 // The standard security handler is built on MD5 and RC4; the format, not this code, chose them.
internal sealed class PdfStandardEncryptor : IPdfObjectEncryptor
{
    /// <summary>
    /// The permission flags that grant everything: every bit set but the two the format keeps clear.
    /// </summary>
    public const int AllPermissions = -4;

    private static readonly byte[] _aesSalt = "sAlT"u8.ToArray();

    private readonly PdfEncryptionMethod _method;
    private readonly string _userPassword;
    private readonly string _ownerPassword;
    private readonly int _permissions;
    private byte[] _fileKey;
    private byte[] _owner;
    private byte[] _user;
    private byte[] _ownerKey;
    private byte[] _userKey;
    private byte[] _permissionsEntry;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfStandardEncryptor"/> class.
    /// </summary>
    /// <param name="method">The encryption.</param>
    /// <param name="userPassword">The password that opens the file; empty lets anyone open it.</param>
    /// <param name="ownerPassword">The password that lifts the permissions; empty makes it the user password.</param>
    /// <param name="permissions">The permission flags, as the <c>/P</c> entry holds them.</param>
    public PdfStandardEncryptor(PdfEncryptionMethod method, string userPassword, string ownerPassword, int permissions = AllPermissions)
    {
        _method = method;
        _userPassword = userPassword ?? string.Empty;
        _ownerPassword = string.IsNullOrEmpty(ownerPassword)
            ? _userPassword
            : ownerPassword;

        // The two lowest bits are reserved and must be clear.
        _permissions = permissions & ~3;
    }

    /// <inheritdoc/>
    public double MinimumVersion => _method switch
    {
        PdfEncryptionMethod.Aes256 => 1.7,
        PdfEncryptionMethod.Aes128 => 1.6,
        _ => 1.4,
    };

    private int Revision => _method switch
    {
        PdfEncryptionMethod.Aes256 => 6,
        PdfEncryptionMethod.Aes128 => 4,
        _ => 3,
    };

    /// <inheritdoc/>
    public void Prepare(byte[] fileIdentifier)
    {
        if (_method == PdfEncryptionMethod.Aes256)
        {
            PrepareAes256();

            return;
        }

        // The owner entry is the padded user password encrypted with the owner password's key.
        var ownerKey = PdfStandardSecurity.OwnerKey(_ownerPassword, Revision, 16);
        var owner = PdfStandardSecurity.Rc4(ownerKey, PdfStandardSecurity.Pad(PdfStandardSecurity.Latin1Password(_userPassword)));

        for (var round = 1; round <= 19; round++)
        {
            owner = PdfStandardSecurity.Rc4(PdfStandardSecurity.Xor(ownerKey, round), owner);
        }

        _owner = owner;
        _fileKey = PdfStandardSecurity.FileKey(PdfStandardSecurity.Latin1Password(_userPassword), _owner, _permissions, fileIdentifier, Revision, 16, encryptMetadata: true);
        _user = PdfStandardSecurity.UserEntry(_fileKey, fileIdentifier, Revision);
    }

    /// <inheritdoc/>
    public byte[] EncryptString(byte[] value, int objectNumber)
    {
        return Encrypt(value, objectNumber);
    }

    /// <inheritdoc/>
    public byte[] EncryptStream(PdfDictionary stream, byte[] value, int objectNumber)
    {
        return Encrypt(value, objectNumber);
    }

    /// <inheritdoc/>
    public PdfDictionary CreateEncryptionDictionary()
    {
        var dictionary = new PdfDictionary(null);

        dictionary.Elements.SetName("/Filter", "/Standard");
        dictionary.Elements["/O"] = new PdfString(_owner, isHex: true);
        dictionary.Elements["/U"] = new PdfString(_user, isHex: true);
        dictionary.Elements.SetInteger("/P", _permissions);

        if (_method == PdfEncryptionMethod.Rc4With128Bits)
        {
            dictionary.Elements.SetInteger("/V", 2);
            dictionary.Elements.SetInteger("/R", 3);
            dictionary.Elements.SetInteger("/Length", 128);

            return dictionary;
        }

        var aes256 = _method == PdfEncryptionMethod.Aes256;
        var filter = new PdfDictionary(null);

        filter.Elements.SetName("/Type", "/CryptFilter");
        filter.Elements.SetName("/AuthEvent", "/DocOpen");
        filter.Elements.SetName("/CFM", aes256 ? "/AESV3" : "/AESV2");
        filter.Elements.SetInteger("/Length", aes256 ? 32 : 16);

        var filters = new PdfDictionary(null);

        filters.Elements["/StdCF"] = filter;
        dictionary.Elements.SetInteger("/V", aes256 ? 5 : 4);
        dictionary.Elements.SetInteger("/R", Revision);
        dictionary.Elements.SetInteger("/Length", aes256 ? 256 : 128);
        dictionary.Elements["/CF"] = filters;
        dictionary.Elements.SetName("/StmF", "/StdCF");
        dictionary.Elements.SetName("/StrF", "/StdCF");
        dictionary.Elements.SetBoolean("/EncryptMetadata", true);

        if (aes256)
        {
            dictionary.Elements["/OE"] = new PdfString(_ownerKey, isHex: true);
            dictionary.Elements["/UE"] = new PdfString(_userKey, isHex: true);
            dictionary.Elements["/Perms"] = new PdfString(_permissionsEntry, isHex: true);
        }

        return dictionary;
    }

    private void PrepareAes256()
    {
        _fileKey = RandomNumberGenerator.GetBytes(32);

        var user = PdfStandardSecurity.Utf8Password(_userPassword);
        var owner = PdfStandardSecurity.Utf8Password(_ownerPassword);
        var userValidation = RandomNumberGenerator.GetBytes(8);
        var userSalt = RandomNumberGenerator.GetBytes(8);

        _user = [.. PdfStandardSecurity.HardenedHash(user, userValidation, []), .. userValidation, .. userSalt];
        _userKey = WrapKey(PdfStandardSecurity.HardenedHash(user, userSalt, []));

        var ownerValidation = RandomNumberGenerator.GetBytes(8);
        var ownerSalt = RandomNumberGenerator.GetBytes(8);

        _owner = [.. PdfStandardSecurity.HardenedHash(owner, ownerValidation, _user), .. ownerValidation, .. ownerSalt];
        _ownerKey = WrapKey(PdfStandardSecurity.HardenedHash(owner, ownerSalt, _user));

        // The permissions are repeated, encrypted with the file key, so a reader can tell they were not changed.
        var permissions = new byte[16];

        BinaryPrimitives.WriteInt32LittleEndian(permissions, _permissions);
        permissions[4] = 0xFF;
        permissions[5] = 0xFF;
        permissions[6] = 0xFF;
        permissions[7] = 0xFF;
        permissions[8] = (byte)'T';
        Encoding.ASCII.GetBytes("adb", permissions.AsSpan(9));
        RandomNumberGenerator.Fill(permissions.AsSpan(12));

        using var aes = Aes.Create();

        aes.Key = _fileKey;
        _permissionsEntry = aes.EncryptEcb(permissions, PaddingMode.None);
    }

    private byte[] WrapKey(byte[] key)
    {
        using var aes = Aes.Create();

        aes.Key = key;

        return aes.EncryptCbc(_fileKey, new byte[16], PaddingMode.None);
    }

    private byte[] Encrypt(byte[] value, int objectNumber)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (_method == PdfEncryptionMethod.Aes256)
        {
            return EncryptAes(_fileKey, value);
        }

        // Revisions 3 and 4 encrypt every object with its own key, made from the file key and the object number.
        var input = new List<byte>(_fileKey.Length + 9);

        input.AddRange(_fileKey);
        input.Add((byte)objectNumber);
        input.Add((byte)(objectNumber >> 8));
        input.Add((byte)(objectNumber >> 16));
        input.Add(0);
        input.Add(0);

        if (_method == PdfEncryptionMethod.Aes128)
        {
            input.AddRange(_aesSalt);
        }

        var key = MD5.HashData([.. input])[..Math.Min(_fileKey.Length + 5, 16)];

        return _method == PdfEncryptionMethod.Aes128
            ? EncryptAes(key, value)
            : PdfStandardSecurity.Rc4(key, value);
    }

    private static byte[] EncryptAes(byte[] key, byte[] value)
    {
        using var aes = Aes.Create();

        aes.Key = key;

        // Each string and stream starts with the random initialisation vector it was encrypted with.
        var vector = RandomNumberGenerator.GetBytes(16);

        return [.. vector, .. aes.EncryptCbc(value, vector, PaddingMode.PKCS7)];
    }
}
#pragma warning restore CA5351
