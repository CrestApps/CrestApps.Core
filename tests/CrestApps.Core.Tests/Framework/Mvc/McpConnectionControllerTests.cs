using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Mvc.Web.Areas.Mcp.Controllers;
using CrestApps.Core.Mvc.Web.Areas.Mcp.ViewModels;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class McpConnectionControllerTests
{
    [Fact]
    public async Task Create_WithAnonymousSseAuthentication_CreatesConnection()
    {
        var catalog = new Mock<ICatalog<McpConnection>>();
        McpConnection createdConnection = null;
        catalog.Setup(store => store.CreateAsync(It.IsAny<McpConnection>(), It.IsAny<CancellationToken>()))
            .Callback<McpConnection, CancellationToken>((connection, _) => createdConnection = connection);
        var controller = new McpConnectionController(catalog.Object, new PassthroughDataProtectionProvider(), TimeProvider.System);
        var model = new McpConnectionViewModel
        {
            DisplayText = "Local MCP",
            Source = McpConstants.TransportTypes.Sse,
            Endpoint = "https://localhost:5100/Mcp/McpConnection/",
            AuthenticationType = ClientAuthenticationType.Anonymous,
        };

        var result = await controller.Create(model);

        var redirectResult = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirectResult.ActionName);
        Assert.True(controller.ModelState.IsValid);
        Assert.NotNull(createdConnection);
        Assert.Equal("Local MCP", createdConnection.DisplayText);
        Assert.Equal(McpConstants.TransportTypes.Sse, createdConnection.Source);
        Assert.True(createdConnection.TryGet<SseMcpConnectionMetadata>(out var metadata));
        Assert.Equal(ClientAuthenticationType.Anonymous, metadata.AuthenticationType);
        Assert.Equal(new Uri("https://localhost:5100/Mcp/McpConnection/"), metadata.Endpoint);
        catalog.Verify(store => store.CreateAsync(It.IsAny<McpConnection>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_WithUndefinedAuthenticationType_ReturnsValidationError()
    {
        var catalog = new Mock<ICatalog<McpConnection>>();
        var controller = new McpConnectionController(catalog.Object, new PassthroughDataProtectionProvider(), TimeProvider.System);
        var model = new McpConnectionViewModel
        {
            DisplayText = "Local MCP",
            Source = McpConstants.TransportTypes.Sse,
            Endpoint = "https://localhost:5100/Mcp/McpConnection/",
            AuthenticationType = (ClientAuthenticationType)999,
        };

        var result = await controller.Create(model);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Same(model, viewResult.Model);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(controller.ModelState[nameof(McpConnectionViewModel.AuthenticationType)].Errors, error => error.ErrorMessage == "Authentication type is not supported.");
        catalog.Verify(store => store.CreateAsync(It.IsAny<McpConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class PassthroughDataProtectionProvider : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose) => new PassthroughDataProtector();
    }

    private sealed class PassthroughDataProtector : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;

        public byte[] Protect(byte[] plaintext) => plaintext;

        public byte[] Unprotect(byte[] protectedData) => protectedData;
    }
}
