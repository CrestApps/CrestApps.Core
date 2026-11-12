using Renci.SshNet;

namespace CrestApps.Core.AI.Sftp;

/// <summary>
/// Checks an SFTP server's host key against the fingerprint stored with the connection. SSH.NET trusts any host key
/// when nothing listens to <see cref="BaseClient.HostKeyReceived"/>, which lets anyone who can intercept the connection
/// pose as the server and read the credentials sent to it, so a connection is only opened when a fingerprint is
/// configured and the server's key matches it.
/// </summary>
public static class SftpHostKeys
{
    /// <summary>
    /// The message used when a connection has no host key fingerprint to check the server against.
    /// </summary>
    public const string MissingFingerprintMessage = "An SFTP host key fingerprint is required. Configure the server's host key fingerprint (for example SHA256:...) on the connection so the server can be verified.";

    private static readonly char[] _separators = [',', ';', ' ', '\t', '\r', '\n'];

    /// <summary>
    /// Determines whether a fingerprint is configured.
    /// </summary>
    /// <param name="configuredFingerprints">The configured fingerprint, or several separated by commas, semicolons or whitespace.</param>
    /// <returns><see langword="true"/> when there is at least one fingerprint.</returns>
    public static bool IsConfigured(string configuredFingerprints)
    {
        return !string.IsNullOrWhiteSpace(configuredFingerprints);
    }

    /// <summary>
    /// Determines whether a server's host key matches one of the configured fingerprints. Fingerprints can be written
    /// the OpenSSH way (<c>SHA256:</c> followed by base64, with or without the padding), or as the legacy MD5 hex form
    /// with or without colons (<c>MD5:</c> prefix optional). Nothing matches when none is configured.
    /// </summary>
    /// <param name="configuredFingerprints">The configured fingerprint, or several separated by commas, semicolons or whitespace.</param>
    /// <param name="sha256Fingerprint">The server key's SHA-256 fingerprint, as base64 without padding.</param>
    /// <param name="md5Fingerprint">The server key's MD5 fingerprint.</param>
    /// <returns><see langword="true"/> when the key is the one that was configured.</returns>
    public static bool Matches(string configuredFingerprints, string sha256Fingerprint, byte[] md5Fingerprint)
    {
        if (!IsConfigured(configuredFingerprints))
        {
            return false;
        }

        var serverSha256 = sha256Fingerprint?.TrimEnd('=');
        var serverMd5 = md5Fingerprint is null ? null : Convert.ToHexString(md5Fingerprint);

        foreach (var entry in configuredFingerprints.Split(_separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (entry.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
            {
                if (SameBase64(entry["SHA256:".Length..], serverSha256))
                {
                    return true;
                }

                continue;
            }

            var hex = (entry.StartsWith("MD5:", StringComparison.OrdinalIgnoreCase) ? entry["MD5:".Length..] : entry).Replace(":", string.Empty, StringComparison.Ordinal);

            if (hex.Length == 32 && IsHex(hex))
            {
                if (serverMd5 is not null && string.Equals(hex, serverMd5, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                continue;
            }

            if (SameBase64(entry, serverSha256))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Makes a client refuse any server whose host key is not one of the configured fingerprints.
    /// </summary>
    /// <param name="client">The client, before it connects.</param>
    /// <param name="configuredFingerprints">The configured fingerprint, or several separated by commas, semicolons or whitespace.</param>
    public static void Require(BaseClient client, string configuredFingerprints)
    {
        ArgumentNullException.ThrowIfNull(client);

        client.HostKeyReceived += (_, args) => args.CanTrust = Matches(configuredFingerprints, args.FingerPrintSHA256, args.FingerPrint);
    }

    private static bool SameBase64(string configured, string server)
    {
        return server is not null && string.Equals(configured?.TrimEnd('='), server, StringComparison.Ordinal);
    }

    private static bool IsHex(string value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
