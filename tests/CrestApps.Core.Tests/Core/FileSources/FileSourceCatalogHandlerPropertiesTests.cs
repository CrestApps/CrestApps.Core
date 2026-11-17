using System.Text.Json.Nodes;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.FileSources.Handlers;
using CrestApps.Core.AI.Ftp.Models;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Sftp.Models;
using CrestApps.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.FileSources;

/// <summary>
/// Covers how the file source catalog handler applies the <c>Properties</c> of incoming JSON, which is where a
/// recipe, a deployment import or an API call carries the connection of an FTP or SFTP source.
/// </summary>
public sealed class FileSourceCatalogHandlerPropertiesTests
{
    /// <summary>
    /// Verifies that a record created from JSON gets the connection the JSON carries, instead of none.
    /// </summary>
    [Fact]
    public async Task Initializing_WithProperties_AppliesTheConnection()
    {
        var fileSource = new FileSource();
        var data = JsonNode.Parse("""
            {
              "DisplayText": "Partner drop",
              "Source": "Sftp",
              "Properties": {
                "SftpConnectionMetadata": {
                  "Host": "sftp.example.com",
                  "Port": 2222,
                  "Username": "reader",
                  "HostKeyFingerprint": "SHA256:abc",
                  "Password": "protected-secret"
                }
              }
            }
            """);

        await CreateHandler().InitializingAsync(new InitializingContext<FileSource>(fileSource, data), TestContext.Current.CancellationToken);

        Assert.True(fileSource.TryGet<SftpConnectionMetadata>(out var connection));
        Assert.Equal("sftp.example.com", connection.Host);
        Assert.Equal(2222, connection.Port);
        Assert.Equal("reader", connection.Username);
        Assert.Equal("SHA256:abc", connection.HostKeyFingerprint);
        Assert.Equal("protected-secret", connection.Password);
    }

    /// <summary>
    /// Verifies that an update moves the connection to the host it names, while the fields and entries it
    /// leaves out keep their stored values.
    /// </summary>
    [Fact]
    public async Task Updating_WithPartialProperties_MergesOverTheStoredEntries()
    {
        var fileSource = CreateStoredSource();
        var data = JsonNode.Parse("""
            {
              "Properties": {
                "SftpConnectionMetadata": {
                  "Host": "new.example.com"
                }
              }
            }
            """);

        await CreateHandler().UpdatingAsync(new UpdatingContext<FileSource>(fileSource, data), TestContext.Current.CancellationToken);

        Assert.True(fileSource.TryGet<SftpConnectionMetadata>(out var connection));
        Assert.Equal("new.example.com", connection.Host);
        Assert.Equal(22, connection.Port);
        Assert.Equal("reader", connection.Username);
        Assert.Equal("protected-secret", connection.Password);
        Assert.True(fileSource.TryGet<FileSourceMetadata>(out var settings));
        Assert.Equal("vision", settings.VisionDeploymentName);
    }

    /// <summary>
    /// Verifies that JSON without <c>Properties</c> leaves the stored connection alone.
    /// </summary>
    [Fact]
    public async Task Updating_WithoutProperties_KeepsTheStoredConnection()
    {
        var fileSource = CreateStoredSource();
        var data = JsonNode.Parse("""{ "DisplayText": "Renamed" }""");

        await CreateHandler().UpdatingAsync(new UpdatingContext<FileSource>(fileSource, data), TestContext.Current.CancellationToken);

        Assert.Equal("Renamed", fileSource.DisplayText);
        Assert.True(fileSource.TryGet<SftpConnectionMetadata>(out var connection));
        Assert.Equal("old.example.com", connection.Host);
        Assert.Equal("protected-secret", connection.Password);
    }

    /// <summary>
    /// Verifies that an entry is matched without regard to case, as the properties dictionary is, so a
    /// camel-cased payload updates the stored entry instead of adding a second one beside it.
    /// </summary>
    [Fact]
    public async Task Updating_WithDifferentlyCasedKeys_UpdatesTheStoredEntry()
    {
        var fileSource = CreateStoredSource();
        var data = JsonNode.Parse("""
            {
              "Properties": {
                "sftpConnectionMetadata": {
                  "host": "new.example.com"
                }
              }
            }
            """);

        await CreateHandler().UpdatingAsync(new UpdatingContext<FileSource>(fileSource, data), TestContext.Current.CancellationToken);

        Assert.Single(fileSource.Properties.Keys, key => key.Equals(nameof(SftpConnectionMetadata), StringComparison.OrdinalIgnoreCase));
        Assert.True(fileSource.TryGet<SftpConnectionMetadata>(out var connection));
        Assert.Equal("new.example.com", connection.Host);
        Assert.Equal("protected-secret", connection.Password);
    }

    /// <summary>
    /// Verifies that an entry sent as null is removed, which is how a payload switches a source from one
    /// connector's connection to another's.
    /// </summary>
    [Fact]
    public async Task Updating_WithNullEntry_RemovesIt()
    {
        var fileSource = CreateStoredSource();
        var data = JsonNode.Parse("""
            {
              "Source": "Ftp",
              "Properties": {
                "SftpConnectionMetadata": null,
                "FtpConnectionMetadata": {
                  "Host": "ftp.example.com",
                  "Username": "reader"
                }
              }
            }
            """);

        await CreateHandler().UpdatingAsync(new UpdatingContext<FileSource>(fileSource, data), TestContext.Current.CancellationToken);

        Assert.False(fileSource.Has<SftpConnectionMetadata>());
        Assert.True(fileSource.TryGet<FtpConnectionMetadata>(out var connection));
        Assert.Equal("ftp.example.com", connection.Host);
        Assert.True(fileSource.Has<FileSourceMetadata>());
    }

    private static FileSource CreateStoredSource()
    {
        var fileSource = new FileSource
        {
            ItemId = "file-source-1",
            DisplayText = "Partner drop",
            Source = "Sftp",
        };

        fileSource.Put(new SftpConnectionMetadata
        {
            Host = "old.example.com",
            Port = 22,
            Username = "reader",
            Password = "protected-secret",
        });

        fileSource.Put(new FileSourceMetadata
        {
            VisionDeploymentName = "vision",
        });

        return fileSource;
    }

    private static FileSourceCatalogHandler CreateHandler()
    {
        return new FileSourceCatalogHandler(
            Mock.Of<IHttpContextAccessor>(),
            TimeProvider.System,
            Mock.Of<IAIDataSourceStore>(),
            Mock.Of<IAIDataSourceIndexingQueue>(),
            Mock.Of<IIngestionItemStateStore>(),
            Mock.Of<IIngestionConnectorResolver>(),
            NullLogger<FileSourceCatalogHandler>.Instance);
    }
}
