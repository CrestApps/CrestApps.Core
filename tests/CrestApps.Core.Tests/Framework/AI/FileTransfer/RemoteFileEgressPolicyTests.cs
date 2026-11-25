using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CrestApps.Core.AI.FileSources.FileTransfer;
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

public sealed class RemoteFileEgressPolicyTests
{
    // Names under .invalid never resolve, so a test only reaches its listener when the client used the address the
    // policy returned instead of looking the name up itself.
    private const string ServerHost = "files.example.invalid";

    private const string ProxyHost = "proxy.example.invalid";

    private static readonly TimeSpan _acceptTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task DefaultPolicy_ReturnsAnAddressAsItIsGiven()
    {
        var address = await new DefaultRemoteFileEgressPolicy().ResolveAsync("10.1.2.3", 21, TestContext.Current.CancellationToken);

        Assert.Equal(IPAddress.Parse("10.1.2.3"), address);
    }

    [Fact]
    public async Task DefaultPolicy_ResolvesAHostName()
    {
        var address = await new DefaultRemoteFileEgressPolicy().ResolveAsync("localhost", 21, TestContext.Current.CancellationToken);

        Assert.True(IPAddress.IsLoopback(address));
    }

    [Fact]
    public async Task FtpFactory_ConnectsToTheAddressThePolicyReturned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var listener = StartListener();
        var port = GetPort(listener);
        var policy = new LoopbackEgressPolicy();
        var factory = CreateFtpFactory(policy);
        var source = new FileSource { ItemId = "source-1" };
        source.Put(new FtpConnectionMetadata { Host = ServerHost, Port = port, ConnectTimeout = 5, ReadTimeout = 5 });

        var connecting = Task.Run(() => factory.CreateAsync(source, cancellationToken), cancellationToken);

        using (await AcceptAsync(listener, cancellationToken))
        {
        }

        await Record.ExceptionAsync(() => connecting);

        Assert.Equal([(ServerHost, port)], policy.Requests);
    }

    [Fact]
    public async Task FtpFactory_DoesNotConnectWhenThePolicyRefuses()
    {
        using var listener = StartListener();
        var factory = CreateFtpFactory(new RefusingEgressPolicy());
        var source = new FileSource { ItemId = "source-1" };
        source.Put(new FtpConnectionMetadata { Host = "127.0.0.1", Port = GetPort(listener), ConnectTimeout = 5 });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync(source, TestContext.Current.CancellationToken));

        Assert.Equal(RefusingEgressPolicy.Message, exception.Message);
        Assert.False(listener.Pending());
    }

    [Fact]
    public async Task FtpResourceHandler_ConnectsToTheAddressThePolicyReturned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var listener = StartListener();
        var port = GetPort(listener);
        var policy = new LoopbackEgressPolicy();
        var handler = new FtpResourceTypeHandler(
            new EphemeralDataProtectionProvider(),
            Options.Create(new FtpOptions()),
            policy,
            NullLogger<FtpResourceTypeHandler>.Instance);
        var resource = CreateResource(new FtpConnectionMetadata { Host = ServerHost, Port = port, ConnectTimeout = 5, ReadTimeout = 5 });

        var reading = Task.Run(() => handler.ReadAsync(resource, new Dictionary<string, string> { ["path"] = "file.txt" }, cancellationToken), cancellationToken);

        using (await AcceptAsync(listener, cancellationToken))
        {
        }

        await Record.ExceptionAsync(() => reading);

        Assert.Equal([(ServerHost, port)], policy.Requests);
    }

    [Theory]
    [InlineData("PASV")]
    [InlineData("EPSV")]
    public async Task FtpDataConnection_StaysOnTheCheckedAddressWhateverTheServerAnswers(string dataConnectionType)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var control = StartListener();
        using var data = StartListener();
        var factory = CreateFtpFactory(new LoopbackEgressPolicy());
        var source = new FileSource { ItemId = "source-1" };
        source.Put(new FtpConnectionMetadata { Host = ServerHost, Port = GetPort(control), DataConnectionType = dataConnectionType, ConnectTimeout = 5, ReadTimeout = 5 });

        var server = ServeOneListingAsync(control, data, cancellationToken);

        await using (var client = await factory.CreateAsync(source, cancellationToken))
        {
            await client.ListAsync("/", false, 0, cancellationToken);
        }

        Assert.True(await server);
    }

    [Fact]
    public async Task SftpFactory_ConnectsToTheAddressThePolicyReturned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var listener = StartListener();
        var port = GetPort(listener);
        var policy = new LoopbackEgressPolicy();
        var factory = new SftpRemoteFileClientFactory(new EphemeralDataProtectionProvider(), policy, NullLogger<SftpRemoteFileClientFactory>.Instance);
        var source = new FileSource { ItemId = "source-1" };
        source.Put(CreateSftpMetadata(port));

        var connecting = Task.Run(() => factory.CreateAsync(source, cancellationToken), cancellationToken);

        using (await AcceptAsync(listener, cancellationToken))
        {
        }

        await Record.ExceptionAsync(() => connecting);

        Assert.Equal([(ServerHost, port)], policy.Requests);
    }

    [Fact]
    public async Task SftpResourceHandler_ConnectsToTheAddressThePolicyReturned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var listener = StartListener();
        var port = GetPort(listener);
        var policy = new LoopbackEgressPolicy();
        var handler = new SftpResourceTypeHandler(new EphemeralDataProtectionProvider(), policy, NullLogger<SftpResourceTypeHandler>.Instance);
        var resource = CreateResource(CreateSftpMetadata(port));

        var reading = Task.Run(() => handler.ReadAsync(resource, new Dictionary<string, string> { ["path"] = "file.txt" }, cancellationToken), cancellationToken);

        using (await AcceptAsync(listener, cancellationToken))
        {
        }

        await Record.ExceptionAsync(() => reading);

        Assert.Equal([(ServerHost, port)], policy.Requests);
    }

    [Fact]
    public async Task SftpFactory_ConnectsThroughTheProxyAndAsksItForTheCheckedServerAddress()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var proxy = StartListener();
        var proxyPort = GetPort(proxy);
        var policy = new LoopbackEgressPolicy();
        var factory = new SftpRemoteFileClientFactory(new EphemeralDataProtectionProvider(), policy, NullLogger<SftpRemoteFileClientFactory>.Instance);
        var metadata = CreateSftpMetadata(2222);
        metadata.ProxyType = "Http";
        metadata.ProxyHost = ProxyHost;
        metadata.ProxyPort = proxyPort;
        var source = new FileSource { ItemId = "source-1" };
        source.Put(metadata);

        var connecting = Task.Run(() => factory.CreateAsync(source, cancellationToken), cancellationToken);

        string request;

        using (var accepted = await AcceptAsync(proxy, cancellationToken))
        {
            using var reader = new StreamReader(accepted.GetStream(), Encoding.ASCII);
            request = await reader.ReadLineAsync(cancellationToken);
        }

        await Record.ExceptionAsync(() => connecting);

        Assert.Equal("CONNECT 127.0.0.1:2222 HTTP/1.0", request);
        Assert.Equal([(ProxyHost, proxyPort), (ServerHost, 2222)], policy.Requests);
    }

    [Fact]
    public async Task SftpFactory_RefusesAProxyWithoutAHostAndPort()
    {
        var policy = new LoopbackEgressPolicy();
        var factory = new SftpRemoteFileClientFactory(new EphemeralDataProtectionProvider(), policy, NullLogger<SftpRemoteFileClientFactory>.Instance);
        var metadata = CreateSftpMetadata(2222);
        metadata.ProxyType = "Socks5";
        var source = new FileSource { ItemId = "source-1" };
        source.Put(metadata);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync(source, TestContext.Current.CancellationToken));

        Assert.Contains("proxy", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(policy.Requests);
    }

    [Fact]
    public async Task SftpFactory_ConnectsWithoutAProxyWhenTheProxyTypeIsUnknown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var listener = StartListener();
        var port = GetPort(listener);
        var policy = new LoopbackEgressPolicy();
        var factory = new SftpRemoteFileClientFactory(new EphemeralDataProtectionProvider(), policy, NullLogger<SftpRemoteFileClientFactory>.Instance);
        var metadata = CreateSftpMetadata(port);
        metadata.ProxyType = "not-a-proxy-type";
        metadata.ProxyHost = ProxyHost;
        metadata.ProxyPort = 1;
        var source = new FileSource { ItemId = "source-1" };
        source.Put(metadata);

        var connecting = Task.Run(() => factory.CreateAsync(source, cancellationToken), cancellationToken);

        using (await AcceptAsync(listener, cancellationToken))
        {
        }

        await Record.ExceptionAsync(() => connecting);

        Assert.Equal([(ServerHost, port)], policy.Requests);
    }

    // Answers just enough FTP for FluentFTP to log in and list a folder. Every passive answer names 169.254.169.254, the
    // cloud metadata address, as the place for the data connection, so the data listener only sees a connection when the
    // client ignored that and stayed on the address it connected to.
    private static async Task<bool> ServeOneListingAsync(TcpListener control, TcpListener data, CancellationToken cancellationToken)
    {
        var dataPort = GetPort(data);
        var passiveAddress = $"169,254,169,254,{dataPort / 256},{dataPort % 256}";
        var dataConnectionReceived = false;

        using var client = await AcceptAsync(control, cancellationToken);
        using var reader = new StreamReader(client.GetStream(), Encoding.ASCII);
        using var writer = new StreamWriter(client.GetStream(), Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

        await writer.WriteLineAsync("220 Ready");

        string line;

        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            var command = line.Split(' ')[0].ToUpperInvariant();

            if (command == "QUIT")
            {
                await writer.WriteLineAsync("221 Bye");

                break;
            }

            if (command == "LIST" || command == "MLSD" || command == "NLST")
            {
                await writer.WriteLineAsync("150 Sending the listing");

                using (await AcceptAsync(data, cancellationToken))
                {
                    dataConnectionReceived = true;
                }

                await writer.WriteLineAsync("226 Done");

                continue;
            }

            await writer.WriteLineAsync(command switch
            {
                "USER" => "230 Logged in",
                "PWD" => "257 \"/\" is the current directory",
                "PASV" => $"227 Entering Passive Mode ({passiveAddress})",
                "EPSV" => $"229 Entering Extended Passive Mode ({passiveAddress})",
                _ => "200 OK",
            });
        }

        return dataConnectionReceived;
    }

    private static FtpRemoteFileClientFactory CreateFtpFactory(IRemoteFileEgressPolicy policy)
    {
        return new FtpRemoteFileClientFactory(
            new EphemeralDataProtectionProvider(),
            Options.Create(new FtpOptions()),
            policy,
            NullLogger<FtpRemoteFileClientFactory>.Instance);
    }

    private static SftpConnectionMetadata CreateSftpMetadata(int port)
    {
        return new SftpConnectionMetadata
        {
            Host = ServerHost,
            Port = port,
            Username = "user",
            Password = "password",
            HostKeyFingerprint = "SHA256:not-the-server-key",
            ConnectionTimeout = 5,
        };
    }

    private static TcpListener StartListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        return listener;
    }

    private static int GetPort(TcpListener listener)
    {
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<TcpClient> AcceptAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        return await listener.AcceptTcpClientAsync(cancellationToken).AsTask().WaitAsync(_acceptTimeout, cancellationToken);
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

    private sealed class LoopbackEgressPolicy : IRemoteFileEgressPolicy
    {
        private readonly ConcurrentQueue<(string Host, int Port)> _requests = new();

        public IEnumerable<(string Host, int Port)> Requests => _requests;

        public Task<IPAddress> ResolveAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            _requests.Enqueue((host, port));

            return Task.FromResult(IPAddress.Loopback);
        }
    }

    private sealed class RefusingEgressPolicy : IRemoteFileEgressPolicy
    {
        public const string Message = "The test policy refuses every address.";

        public Task<IPAddress> ResolveAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(Message);
        }
    }
}
