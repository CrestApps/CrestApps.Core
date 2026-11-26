using CrestApps.Core.AI.Ftp;
using CrestApps.Core.AI.Ftp.Models;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Sftp;
using CrestApps.Core.AI.Sftp.Models;
using CrestApps.Core.Mvc.Web.Areas.Mcp.Controllers;
using CrestApps.Core.Mvc.Web.Areas.Mcp.ViewModels;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class McpResourceControllerTests
{
    private const string ResourceId = "resource-1";

    [Fact]
    public async Task Edit_FtpWithAUsernameAndABlankPassword_KeepsTheStoredPassword()
    {
        var resource = CreateResource(FtpResourceConstants.Type);
        resource.Put(new FtpConnectionMetadata { Host = "files.example.com", Username = "demo", Password = "stored-password" });
        var (controller, catalog) = CreateController(resource);
        var model = CreateModel(FtpResourceConstants.Type);
        model.Username = "demo";

        var result = await controller.Edit(model);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(resource.TryGet<FtpConnectionMetadata>(out var metadata));
        Assert.Equal("stored-password", metadata.Password);
        catalog.Verify(store => store.UpdateAsync(resource, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Edit_SftpWithBlankSecrets_KeepsTheStoredKeyAndProxyPassword()
    {
        var resource = CreateResource(SftpResourceConstants.Type);
        resource.Put(new SftpConnectionMetadata
        {
            Host = "files.example.com",
            Username = "demo",
            PrivateKey = "stored-key",
            ProxyType = "Http",
            ProxyHost = "proxy.example.com",
            ProxyPort = 3128,
            ProxyPassword = "stored-proxy-password",
        });
        var (controller, catalog) = CreateController(resource);
        var model = CreateModel(SftpResourceConstants.Type);
        model.Username = "demo";
        model.ProxyType = "Http";
        model.ProxyHost = "proxy.example.com";
        model.ProxyPort = 3128;

        var result = await controller.Edit(model);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(resource.TryGet<SftpConnectionMetadata>(out var metadata));
        Assert.Equal("stored-key", metadata.PrivateKey);
        Assert.Equal("stored-proxy-password", metadata.ProxyPassword);
        catalog.Verify(store => store.UpdateAsync(resource, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Edit_FtpWithAUsernameAndNoStoredPassword_StillAsksForOne()
    {
        var resource = CreateResource(FtpResourceConstants.Type);
        resource.Put(new FtpConnectionMetadata { Host = "files.example.com" });
        var (controller, catalog) = CreateController(resource);
        var model = CreateModel(FtpResourceConstants.Type);
        model.Username = "demo";

        var result = await controller.Edit(model);

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[nameof(McpResourceViewModel.Password)].Errors, error => error.ErrorMessage == "Password is required when a username is provided.");
        catalog.Verify(store => store.UpdateAsync(It.IsAny<McpResource>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Edit_ChangedFromFtpToSftp_DoesNotCountTheFtpPassword()
    {
        var resource = CreateResource(FtpResourceConstants.Type);
        resource.Put(new FtpConnectionMetadata { Host = "files.example.com", Username = "demo", Password = "stored-password" });
        var (controller, catalog) = CreateController(resource);
        var model = CreateModel(SftpResourceConstants.Type);
        model.Username = "demo";

        var result = await controller.Edit(model);

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[nameof(McpResourceViewModel.Password)].Errors, error => error.ErrorMessage == "Provide a password or private key.");
        catalog.Verify(store => store.UpdateAsync(It.IsAny<McpResource>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static McpResource CreateResource(string source)
    {
        return new McpResource
        {
            ItemId = ResourceId,
            Source = source,
            DisplayText = "Reports",
        };
    }

    private static McpResourceViewModel CreateModel(string source)
    {
        return new McpResourceViewModel
        {
            ItemId = ResourceId,
            Source = source,
            DisplayText = "Reports",
            Name = "reports",
            Path = "{path}",
            Host = "files.example.com",
        };
    }

    private static (McpResourceController Controller, Mock<ISourceCatalog<McpResource>> Catalog) CreateController(McpResource resource)
    {
        var catalog = new Mock<ISourceCatalog<McpResource>>();
        catalog.Setup(store => store.FindByIdAsync(ResourceId, It.IsAny<CancellationToken>())).ReturnsAsync(resource);
        var options = new McpOptions();
        options.AddResourceType(FtpResourceConstants.Type);
        options.AddResourceType(SftpResourceConstants.Type);
        var controller = new McpResourceController(catalog.Object, Options.Create(options), new EphemeralDataProtectionProvider(), TimeProvider.System);

        return (controller, catalog);
    }
}
