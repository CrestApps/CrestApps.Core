using Microsoft.Playwright;
using Xunit;

namespace CrestApps.Core.Tests.Samples.Infrastructure;

public class PlaywrightFixture : IAsyncLifetime
{
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;
    public string BrowserUnavailableReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });
        }
        catch (PlaywrightException ex)
        {
            BrowserUnavailableReason = $"Playwright browser is unavailable. Install the required browser binaries before running sample UI tests. {ex.Message}";
            Playwright?.Dispose();
            Playwright = null!;
            Browser = null!;
        }
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        Playwright?.Dispose();
    }

    public static string GetBaseUrl(AppInstance app) => app switch
    {
        AppInstance.Mvc => TestConstants.MvcBaseUrl,
        AppInstance.Blazor => TestConstants.BlazorBaseUrl,
        _ => throw new ArgumentOutOfRangeException(nameof(app))
    };

    public async Task<IPage> CreatePageAsync()
    {
        if (!string.IsNullOrWhiteSpace(BrowserUnavailableReason))
        {
            Assert.Skip(BrowserUnavailableReason);
        }

        return await Browser.NewPageAsync();
    }

    /// <summary>
    /// Verifies that the specified app is reachable and returns an HTML login page.
    /// Call this at the start of tests that require a running app instance.
    /// </summary>
    public static async Task AssertAppIsReachableAsync(AppInstance app)
    {
        var baseUrl = GetBaseUrl(app);
        var loginPath = app == AppInstance.Mvc ? "/Account/Login" : "/account/login";

        using var client = new HttpClient(new HttpClientHandler
        {
            // Allow self-signed certs for local HTTPS.
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        });
        client.Timeout = TimeSpan.FromSeconds(10);

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync($"{baseUrl}{loginPath}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Cannot reach {app} app at {baseUrl}. " +
                $"Ensure the app is running (dotnet run --project the-project). Error: {ex.Message}");
        }

        var body = await response.Content.ReadAsStringAsync();

        if (!body.Contains("username", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{app} app at {baseUrl}{loginPath} returned HTTP {(int)response.StatusCode} " +
                $"but the response does not contain a login form. " +
                $"Verify the correct URL, port, and protocol.");
        }
    }
}
