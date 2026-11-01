using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.WebMcp.Endpoints;
using CrestApps.Core.AI.WebMcp.Models;
using CrestApps.Core.AI.WebMcp.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;

namespace CrestApps.Core.Tests.Core.WebMcp;

public sealed class WebMcpEndpointsTests
{
    [Fact]
    public async Task GetToolsAsync_RefusesAnonymousUsers()
    {
        var httpContext = CreateHttpContext(authenticated: false);
        var toolService = new Mock<IWebMcpToolService>();

        var result = await WebMcpEndpoints.GetToolsAsync(httpContext, Access(true), toolService.Object);

        Assert.IsType<UnauthorizedHttpResult>(result);
        toolService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetToolsAsync_RefusesUsersTheHostDoesNotAllow()
    {
        var httpContext = CreateHttpContext();
        var toolService = new Mock<IWebMcpToolService>();

        var result = await WebMcpEndpoints.GetToolsAsync(httpContext, Access(false), toolService.Object);

        Assert.IsType<ForbidHttpResult>(result);
        toolService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetToolsAsync_ListsTheUsersTools()
    {
        var httpContext = CreateHttpContext();
        var tools = new List<WebMcpToolDescriptor>
        {
            new()
            {
                Name = "echo_tool",
                Title = "Echo",
                Description = "Echoes.",
            },
        };
        var toolService = new Mock<IWebMcpToolService>();

        toolService
            .Setup(service => service.GetToolsAsync(httpContext.User, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tools);

        var result = await WebMcpEndpoints.GetToolsAsync(httpContext, Access(true), toolService.Object);

        var ok = Assert.IsType<Ok<WebMcpToolListResponse>>(result);
        Assert.Same(tools, ok.Value.Tools);
    }

    [Fact]
    public async Task InvokeAsync_RefusesAnonymousUsers()
    {
        var httpContext = CreateHttpContext(authenticated: false, body: "{}");
        var toolService = new Mock<IWebMcpToolService>();

        var result = await WebMcpEndpoints.InvokeAsync("echo_tool", httpContext, Access(true), Antiforgery(true), toolService.Object);

        Assert.IsType<UnauthorizedHttpResult>(result);
        toolService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_RefusesUsersTheHostDoesNotAllow()
    {
        var httpContext = CreateHttpContext(body: "{}");
        var toolService = new Mock<IWebMcpToolService>();

        var result = await WebMcpEndpoints.InvokeAsync("echo_tool", httpContext, Access(false), Antiforgery(true), toolService.Object);

        Assert.IsType<ForbidHttpResult>(result);
        toolService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_RefusesARequestWithoutAValidAntiforgeryToken()
    {
        var httpContext = CreateHttpContext(body: "{}");
        var toolService = new Mock<IWebMcpToolService>();

        var result = await WebMcpEndpoints.InvokeAsync("echo_tool", httpContext, Access(true), Antiforgery(false), toolService.Object);

        Assert.IsType<BadRequest<string>>(result);
        toolService.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    [InlineData("{broken")]
    public async Task InvokeAsync_RefusesInputThatIsNotAJsonObject(string body)
    {
        var httpContext = CreateHttpContext(body: body);
        var toolService = new Mock<IWebMcpToolService>();

        var result = await WebMcpEndpoints.InvokeAsync("echo_tool", httpContext, Access(true), Antiforgery(true), toolService.Object);

        Assert.IsType<BadRequest<string>>(result);
        toolService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_RefusesInputLargerThanTheLimit()
    {
        var httpContext = CreateHttpContext(body: "{}");

        httpContext.Request.ContentLength = WebMcpEndpoints.MaxRequestBodyLength + 1;

        var toolService = new Mock<IWebMcpToolService>();

        var result = await WebMcpEndpoints.InvokeAsync("echo_tool", httpContext, Access(true), Antiforgery(true), toolService.Object);

        Assert.IsType<BadRequest<string>>(result);
        toolService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_AnswersNotFoundForAToolTheUserCannotCall()
    {
        var httpContext = CreateHttpContext(body: "{}");
        var toolService = new Mock<IWebMcpToolService>();

        toolService
            .Setup(service => service.InvokeAsync(httpContext.User, "missing", It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebMcpToolInvocationResult.NotFound());

        var result = await WebMcpEndpoints.InvokeAsync("missing", httpContext, Access(true), Antiforgery(true), toolService.Object);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task InvokeAsync_CallsTheToolWithTheArgumentsAndAnswersAnMcpResult()
    {
        var httpContext = CreateHttpContext(body: """{"text":"hi","count":2}""");
        IDictionary<string, object> received = null;
        var toolService = new Mock<IWebMcpToolService>();

        toolService
            .Setup(service => service.InvokeAsync(httpContext.User, "echo_tool", It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback((ClaimsPrincipal _, string _, IDictionary<string, object> arguments, CancellationToken _) => received = arguments)
            .ReturnsAsync(WebMcpToolInvocationResult.Success("echo: hi"));

        var result = await WebMcpEndpoints.InvokeAsync("echo_tool", httpContext, Access(true), Antiforgery(true), toolService.Object);

        var ok = Assert.IsType<Ok<WebMcpToolCallResponse>>(result);
        Assert.False(ok.Value.IsError);
        var content = Assert.Single(ok.Value.Content);
        Assert.Equal("text", content.Type);
        Assert.Equal("echo: hi", content.Text);
        Assert.Equal("hi", ((JsonElement)received["text"]).GetString());
        Assert.Equal(2, ((JsonElement)received["count"]).GetInt32());
    }

    [Fact]
    public async Task InvokeAsync_TreatsAnEmptyBodyAsNoArguments()
    {
        var httpContext = CreateHttpContext(body: string.Empty);
        IDictionary<string, object> received = null;
        var toolService = new Mock<IWebMcpToolService>();

        toolService
            .Setup(service => service.InvokeAsync(httpContext.User, "echo_tool", It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback((ClaimsPrincipal _, string _, IDictionary<string, object> arguments, CancellationToken _) => received = arguments)
            .ReturnsAsync(WebMcpToolInvocationResult.Success("ok"));

        var result = await WebMcpEndpoints.InvokeAsync("echo_tool", httpContext, Access(true), Antiforgery(true), toolService.Object);

        Assert.IsType<Ok<WebMcpToolCallResponse>>(result);
        Assert.NotNull(received);
        Assert.Empty(received);
    }

    [Fact]
    public async Task InvokeAsync_ReportsAFailedToolAsAnErrorResult()
    {
        var httpContext = CreateHttpContext(body: "{}");
        var toolService = new Mock<IWebMcpToolService>();

        toolService
            .Setup(service => service.InvokeAsync(httpContext.User, "failing_tool", It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebMcpToolInvocationResult.Failure("The tool failed."));

        var result = await WebMcpEndpoints.InvokeAsync("failing_tool", httpContext, Access(true), Antiforgery(true), toolService.Object);

        var ok = Assert.IsType<Ok<WebMcpToolCallResponse>>(result);
        Assert.True(ok.Value.IsError);
        Assert.Equal("The tool failed.", Assert.Single(ok.Value.Content).Text);
    }

    [Fact]
    public async Task DefaultAccessEvaluator_AllowsSignedInUsersOnly()
    {
        var evaluator = new DefaultWebMcpAccessEvaluator();

        Assert.True(await evaluator.CanUseAsync(CreateHttpContext().User, TestContext.Current.CancellationToken));
        Assert.False(await evaluator.CanUseAsync(CreateHttpContext(authenticated: false).User, TestContext.Current.CancellationToken));
        Assert.False(await evaluator.CanUseAsync(null, TestContext.Current.CancellationToken));
    }

    private static DefaultHttpContext CreateHttpContext(bool authenticated = true, string body = null)
    {
        var identity = authenticated
            ? new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "Test")
            : new ClaimsIdentity();

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
        };

        if (body is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);

            httpContext.Request.Method = HttpMethods.Post;
            httpContext.Request.ContentType = "application/json";
            httpContext.Request.ContentLength = bytes.Length;
            httpContext.Request.Body = new MemoryStream(bytes);
        }

        return httpContext;
    }

    private static IWebMcpAccessEvaluator Access(bool allowed)
    {
        var evaluator = new Mock<IWebMcpAccessEvaluator>();

        evaluator
            .Setup(e => e.CanUseAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(allowed);

        return evaluator.Object;
    }

    private static IAntiforgery Antiforgery(bool valid)
    {
        var antiforgery = new Mock<IAntiforgery>();

        antiforgery
            .Setup(service => service.IsRequestValidAsync(It.IsAny<HttpContext>()))
            .ReturnsAsync(valid);

        return antiforgery.Object;
    }
}
