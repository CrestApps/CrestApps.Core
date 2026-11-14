using System.Security.Cryptography;
using System.Text;
using CrestApps.Core.AI.Ftp;
using CrestApps.Core.AI.Ftp.Handlers;
using CrestApps.Core.AI.Ftp.Models;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Sftp;
using CrestApps.Core.AI.Sftp.Handlers;
using CrestApps.Core.AI.Sftp.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace CrestApps.Core.Tests.Framework.AI.FileTransfer;

public sealed class SftpFtpHardeningTests
{
    private static readonly byte[] _publicKeyBlob = Encoding.UTF8.GetBytes("a host key, for the purposes of these tests");

    private static string Sha256Fingerprint => Convert.ToBase64String(SHA256.HashData(_publicKeyBlob)).TrimEnd('=');

    // Sixteen arbitrary bytes standing in for the server key's MD5 fingerprint; the code under test only compares them.
    private static byte[] Md5Fingerprint => [0x1a, 0x2b, 0x3c, 0x4d, 0x5e, 0x6f, 0x70, 0x81, 0x92, 0xa3, 0xb4, 0xc5, 0xd6, 0xe7, 0xf8, 0x09];

    [Fact]
    public void HostKeys_MatchTheSha256FingerprintInOpenSshForm()
    {
        Assert.True(SftpHostKeys.Matches($"SHA256:{Sha256Fingerprint}", Sha256Fingerprint, Md5Fingerprint));
        Assert.True(SftpHostKeys.Matches(Sha256Fingerprint, Sha256Fingerprint, Md5Fingerprint));
        Assert.True(SftpHostKeys.Matches($"SHA256:{Sha256Fingerprint}=", Sha256Fingerprint, Md5Fingerprint));
    }

    [Fact]
    public void HostKeys_MatchTheMd5FingerprintWithOrWithoutColons()
    {
        var hex = Convert.ToHexString(Md5Fingerprint);
        var colons = string.Join(':', Md5Fingerprint.Select(b => b.ToString("x2")));

        Assert.True(SftpHostKeys.Matches(colons, Sha256Fingerprint, Md5Fingerprint));
        Assert.True(SftpHostKeys.Matches(hex, Sha256Fingerprint, Md5Fingerprint));
        Assert.True(SftpHostKeys.Matches($"MD5:{colons.ToUpperInvariant()}", Sha256Fingerprint, Md5Fingerprint));
    }

    [Fact]
    public void HostKeys_RejectADifferentKey()
    {
        var other = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("another key"))).TrimEnd('=');

        Assert.False(SftpHostKeys.Matches($"SHA256:{other}", Sha256Fingerprint, Md5Fingerprint));
        Assert.False(SftpHostKeys.Matches("00:11:22:33", Sha256Fingerprint, Md5Fingerprint));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void HostKeys_NeverMatchWhenNoFingerprintIsConfigured(string configured)
    {
        Assert.False(SftpHostKeys.Matches(configured, Sha256Fingerprint, Md5Fingerprint));
        Assert.False(SftpHostKeys.IsConfigured(configured));
    }

    [Fact]
    public void HostKeys_AcceptAnyOfSeveralConfiguredFingerprints()
    {
        var other = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("old key"))).TrimEnd('=');

        Assert.True(SftpHostKeys.Matches($"SHA256:{other}, SHA256:{Sha256Fingerprint}", Sha256Fingerprint, Md5Fingerprint));
        Assert.True(SftpHostKeys.Matches($"SHA256:{other};SHA256:{Sha256Fingerprint}", Sha256Fingerprint, Md5Fingerprint));
        Assert.True(SftpHostKeys.IsConfigured(" SHA256:abc "));
    }

    [Fact]
    public async Task SftpFactory_FailsClosedWhenNoHostKeyFingerprintIsConfigured()
    {
        var factory = new SftpRemoteFileClientFactory(new EphemeralDataProtectionProvider(), NullLogger<SftpRemoteFileClientFactory>.Instance);
        var source = new FileSource { ItemId = "source-1" };
        source.Put(new SftpConnectionMetadata { Host = "127.0.0.1", Port = 1, Username = "user", Password = "x" });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync(source, TestContext.Current.CancellationToken));

        Assert.Contains("host key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SftpResourceHandler_FailsClosedWhenNoHostKeyFingerprintIsConfigured()
    {
        var handler = new SftpResourceTypeHandler(new EphemeralDataProtectionProvider(), NullLogger<SftpResourceTypeHandler>.Instance);
        var resource = CreateResource(new SftpConnectionMetadata { Host = "127.0.0.1", Port = 1, Username = "user", Password = "x" });

        var result = await handler.ReadAsync(resource, new Dictionary<string, string>(), TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents)).Text;
        Assert.Contains("host key", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FtpFactory_RefusesToAcceptAnyCertificateWhenTheHostDidNotAllowIt()
    {
        var factory = CreateFtpFactory(allowAnyCertificate: false);
        var source = new FileSource { ItemId = "source-1" };
        source.Put(new FtpConnectionMetadata { Host = "127.0.0.1", Port = 1, ValidateAnyCertificate = true });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync(source, TestContext.Current.CancellationToken));

        Assert.Contains("certificate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FtpFactory_ConnectsWhenTheHostAllowedAnyCertificate()
    {
        var factory = CreateFtpFactory(allowAnyCertificate: true);
        var source = new FileSource { ItemId = "source-1" };
        source.Put(new FtpConnectionMetadata { Host = "127.0.0.1", Port = 1, ValidateAnyCertificate = true, ConnectTimeout = 2 });

        // Nothing listens on the port, so the connection itself fails; what matters is that the policy did not stop it.
        var exception = await Record.ExceptionAsync(() => factory.CreateAsync(source, TestContext.Current.CancellationToken));

        Assert.NotNull(exception);
        Assert.False(exception is InvalidOperationException invalid && invalid.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FtpFactory_DoesNotNeedThePolicyWhenCertificatesAreValidated()
    {
        var factory = CreateFtpFactory(allowAnyCertificate: false);
        var source = new FileSource { ItemId = "source-1" };
        source.Put(new FtpConnectionMetadata { Host = "127.0.0.1", Port = 1, ConnectTimeout = 2 });

        var exception = await Record.ExceptionAsync(() => factory.CreateAsync(source, TestContext.Current.CancellationToken));

        Assert.NotNull(exception);
        Assert.False(exception is InvalidOperationException invalid && invalid.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FtpResourceHandler_RefusesToAcceptAnyCertificateWhenTheHostDidNotAllowIt()
    {
        var handler = new FtpResourceTypeHandler(
            new EphemeralDataProtectionProvider(),
            Options.Create(new FtpOptions { AllowAnyCertificate = false }),
            NullLogger<FtpResourceTypeHandler>.Instance);
        var resource = CreateResource(new FtpConnectionMetadata { Host = "127.0.0.1", Port = 1, ValidateAnyCertificate = true });

        var result = await handler.ReadAsync(resource, new Dictionary<string, string>(), TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents)).Text;
        Assert.Contains("certificate", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FtpOptions_DoNotAllowAnyCertificateByDefault()
    {
        Assert.False(new FtpOptions().AllowAnyCertificate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("None")]
    [InlineData("Auto")]
    [InlineData("not-a-mode")]
    public async Task FtpFactory_RefusesCredentialsOverAnUnencryptedOrAutoConnection(string encryptionMode)
    {
        var factory = CreateFtpFactory(allowAnyCertificate: false);
        var source = new FileSource { ItemId = "source-1" };
        source.Put(new FtpConnectionMetadata { Host = "127.0.0.1", Port = 1, Username = "user", Password = "x", EncryptionMode = encryptionMode, ConnectTimeout = 2 });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync(source, TestContext.Current.CancellationToken));

        Assert.Contains("unencrypted", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Explicit")]
    [InlineData("Implicit")]
    [InlineData("explicit")]
    public async Task FtpFactory_AllowsCredentialsOverAnEncryptedConnection(string encryptionMode)
    {
        var factory = CreateFtpFactory(allowAnyCertificate: false);
        var source = new FileSource { ItemId = "source-1" };
        source.Put(new FtpConnectionMetadata { Host = "127.0.0.1", Port = 1, Username = "user", Password = "x", EncryptionMode = encryptionMode, ConnectTimeout = 2 });

        // Nothing listens on the port, so the connection fails; the policy must not be what stopped it.
        var exception = await Record.ExceptionAsync(() => factory.CreateAsync(source, TestContext.Current.CancellationToken));

        Assert.NotNull(exception);
        Assert.False(exception is InvalidOperationException invalid && invalid.Message.Contains("unencrypted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FtpFactory_AllowsUnencryptedCredentialsWhenTheHostAllowedThem()
    {
        var factory = new FtpRemoteFileClientFactory(
            new EphemeralDataProtectionProvider(),
            Options.Create(new FtpOptions { AllowUnencrypted = true }),
            NullLogger<FtpRemoteFileClientFactory>.Instance);
        var source = new FileSource { ItemId = "source-1" };
        source.Put(new FtpConnectionMetadata { Host = "127.0.0.1", Port = 1, Username = "user", Password = "x", EncryptionMode = "None", ConnectTimeout = 2 });

        var exception = await Record.ExceptionAsync(() => factory.CreateAsync(source, TestContext.Current.CancellationToken));

        Assert.NotNull(exception);
        Assert.False(exception is InvalidOperationException invalid && invalid.Message.Contains("unencrypted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FtpFactory_DoesNotNeedEncryptionForAnAnonymousConnection()
    {
        var factory = CreateFtpFactory(allowAnyCertificate: false);
        var source = new FileSource { ItemId = "source-1" };
        source.Put(new FtpConnectionMetadata { Host = "127.0.0.1", Port = 1, EncryptionMode = "None", ConnectTimeout = 2 });

        var exception = await Record.ExceptionAsync(() => factory.CreateAsync(source, TestContext.Current.CancellationToken));

        Assert.NotNull(exception);
        Assert.False(exception is InvalidOperationException invalid && invalid.Message.Contains("unencrypted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FtpResourceHandler_RefusesCredentialsOverAnUnencryptedConnection()
    {
        var handler = new FtpResourceTypeHandler(
            new EphemeralDataProtectionProvider(),
            Options.Create(new FtpOptions()),
            NullLogger<FtpResourceTypeHandler>.Instance);
        var resource = CreateResource(new FtpConnectionMetadata { Host = "127.0.0.1", Port = 1, Username = "user", Password = "x" });

        var result = await handler.ReadAsync(resource, new Dictionary<string, string>(), TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents)).Text;
        Assert.Contains("unencrypted", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FtpOptions_DoNotAllowUnencryptedCredentialsByDefault()
    {
        Assert.False(new FtpOptions().AllowUnencrypted);
    }

    private static FtpRemoteFileClientFactory CreateFtpFactory(bool allowAnyCertificate)
    {
        return new FtpRemoteFileClientFactory(
            new EphemeralDataProtectionProvider(),
            Options.Create(new FtpOptions { AllowAnyCertificate = allowAnyCertificate }),
            NullLogger<FtpRemoteFileClientFactory>.Instance);
    }

    private static McpResource CreateResource<T>(T metadata)
        where T : class, new()
    {
        var resource = new McpResource
        {
            ItemId = "resource-1",
            Resource = new Resource { Uri = "test://resource", Name = "resource" },
        };

        resource.Put(metadata);

        return resource;
    }
}
