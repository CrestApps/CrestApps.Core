using CrestApps.Core.Startup.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class SampleServerSelectionServiceTests
{
    [Fact]
    public void GetCurrent_ShouldReturnDefaultServerWhenCookieIsMissing()
    {
        var service = CreateService(new Dictionary<string, string>
        {
            ["A2A:DefaultServer"] = "MvcWeb",
            ["A2A:Servers:MvcWeb:DisplayName"] = "MVC Web",
            ["A2A:Servers:MvcWeb:Endpoint"] = "https://localhost:5001",
            ["A2A:Servers:BlazorWeb:DisplayName"] = "Blazor Web",
            ["A2A:Servers:BlazorWeb:Endpoint"] = "https://localhost:5201",
        });

        var context = new DefaultHttpContext();

        var current = service.GetCurrent(context);

        Assert.Equal("MvcWeb", current.Name);
        Assert.Equal("https://localhost:5001", current.Endpoint);
    }

    [Fact]
    public void GetCurrent_ShouldPreferCookieSelectionWhenServerExists()
    {
        var service = CreateService(new Dictionary<string, string>
        {
            ["A2A:DefaultServer"] = "MvcWeb",
            ["A2A:Servers:MvcWeb:DisplayName"] = "MVC Web",
            ["A2A:Servers:MvcWeb:Endpoint"] = "https://localhost:5001",
            ["A2A:Servers:BlazorWeb:DisplayName"] = "Blazor Web",
            ["A2A:Servers:BlazorWeb:Endpoint"] = "https://localhost:5201",
        });

        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "sample-cookie=BlazorWeb";

        var current = service.GetCurrent(context);

        Assert.Equal("BlazorWeb", current.Name);
        Assert.Equal("https://localhost:5201", current.Endpoint);
    }

    [Fact]
    public void GetServers_ShouldFallbackToLegacyEndpointConfiguration()
    {
        var service = CreateService(new Dictionary<string, string>
        {
            ["A2A:Endpoint"] = "https://localhost:5001",
            ["A2A:ApiKey"] = "legacy-key",
        });

        var servers = service.GetServers();

        var server = Assert.Single(servers);
        Assert.Equal("Default", server.Name);
        Assert.Equal("https://localhost:5001", server.Endpoint);
        Assert.Equal("legacy-key", server.ApiKey);
    }

    [Fact]
    public void SetCurrent_ShouldWriteSelectionCookie()
    {
        var service = CreateService(new Dictionary<string, string>
        {
            ["A2A:Servers:MvcWeb:Endpoint"] = "https://localhost:5001",
        });

        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";

        service.SetCurrent(context, "MvcWeb");

        var cookieHeader = Assert.Single(context.Response.Headers.SetCookie);
        Assert.Contains("sample-cookie=MvcWeb", cookieHeader.ToString(), StringComparison.Ordinal);
    }

    private static SampleServerSelectionService CreateService(IDictionary<string, string> configurationValues)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();

return new SampleServerSelectionService(configuration, "A2A", "sample-cookie");
    }
}
