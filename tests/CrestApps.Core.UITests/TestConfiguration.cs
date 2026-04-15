namespace CrestApps.Core.UITests;

/// <summary>
/// Provides base URLs for the MVC and Blazor web applications.
/// Override via environment variables MVC_BASE_URL and BLAZOR_BASE_URL.
/// </summary>
public static class TestConfiguration
{
    public static string MvcBaseUrl =>
        Environment.GetEnvironmentVariable("MVC_BASE_URL") ?? "https://localhost:5001";

    public static string BlazorBaseUrl =>
        Environment.GetEnvironmentVariable("BLAZOR_BASE_URL") ?? "https://localhost:7001";
}
