namespace CrestApps.Core.Tests.Samples.Infrastructure;

public static class TestConstants
{
    public static string MvcBaseUrl => Environment.GetEnvironmentVariable("CRESTAPPS_MVC_BASE_URL") ?? "http://localhost:5101";
    public static string BlazorBaseUrl => Environment.GetEnvironmentVariable("CRESTAPPS_BLAZOR_BASE_URL") ?? "http://localhost:5201";

    public static string TestUsername => Environment.GetEnvironmentVariable("CRESTAPPS_TEST_USERNAME") ?? "admin";
    public static string TestPassword => Environment.GetEnvironmentVariable("CRESTAPPS_TEST_PASSWORD") ?? "Admin123!";
}
