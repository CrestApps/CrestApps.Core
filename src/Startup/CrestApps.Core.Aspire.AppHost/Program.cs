using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// File-based crash log - Console.Error output is lost when the process dies,
// so we write to a persistent file as well.
var crashLogPath = Path.Combine(AppContext.BaseDirectory, "apphost-crash.log");

void WriteCrashEntry(string label, object data)
{
    var message = $"[{DateTime.UtcNow:O}] {label}:{Environment.NewLine}{data}{Environment.NewLine}{Environment.NewLine}";

    try { File.AppendAllText(crashLogPath, message); } catch { }

    Console.Error.Write(message);
}

AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
    WriteCrashEntry($"Unhandled exception (IsTerminating={eventArgs.IsTerminating})", eventArgs.ExceptionObject);
};

TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
{
    if (IsBenignAppHostException(eventArgs.Exception))
    {
        eventArgs.SetObserved();

        return;
    }

    WriteCrashEntry("Unobserved task exception", eventArgs.Exception);
    eventArgs.SetObserved();
};

AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    WriteCrashEntry("Process exit signaled", $"Exit code: {Environment.ExitCode}");
};

var builder = DistributedApplication.CreateBuilder(args);

builder.Services.Configure<HostOptions>(options =>
{
    // Keep the development orchestrator alive when the dashboard loses a watch stream.
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

const string ollamaModelName = "deepseek-v2:16b";

var ollama = builder.AddOllama("Ollama")
    .WithDataVolume()
    .WithGPUSupport()
    .WithHttpEndpoint(port: 11434, targetPort: 11434, name: "HttpOllama");

ollama.AddModel(ollamaModelName);

var redis = builder.AddRedis("Redis");

var mvcWeb = builder.AddProject<Projects.CrestApps_Core_Mvc_Web>("MvcWeb")
    .WithReference(redis)
    .WithReference(ollama)
    .WaitFor(redis)
    .WithHttpsEndpoint(5001, name: "HttpsMvcWeb")
    .WithEnvironment((options) =>
    {
        options.EnvironmentVariables.Add("CrestApps__AI__Providers__Ollama__DefaultDeploymentName", ollamaModelName);
        options.EnvironmentVariables.Add("CrestApps__AI__Providers__Ollama__Connections__Default__Endpoint", "http://localhost:11434");
        options.EnvironmentVariables.Add("CrestApps__AI__Providers__Ollama__Connections__Default__ChatDeploymentName", ollamaModelName);
        options.EnvironmentVariables.Add("CrestApps__MvcApp__MCP__Server__AuthenticationType", "None");
        options.EnvironmentVariables.Add("CrestApps__MvcApp__A2A__Host__AuthenticationType", "None");
        options.EnvironmentVariables.Add("CrestApps__MvcApp__A2A__Host__ExposeAgentsAsSkill", "true");

        // Prevent VS-injected startup hooks (BrowserRefresh, DeltaApplier, BrowserLink)
        // from loading into child processes. These middlewares can interfere with
        // multipart file uploads when running under VS + Aspire.
        options.EnvironmentVariables["DOTNET_STARTUP_HOOKS"] = "";
        options.EnvironmentVariables["__ASPNETCORE_BROWSER_TOOLS"] = "";
    });

var blazorWeb = builder.AddProject<Projects.CrestApps_Core_Blazor_Web>("BlazorWeb")
    .WithReference(redis)
    .WithReference(ollama)
    .WaitFor(redis)
    .WithHttpsEndpoint(5201, name: "HttpsBlazorWeb")
    .WithEnvironment((options) =>
    {
        options.EnvironmentVariables.Add("CrestApps__AI__Providers__Ollama__DefaultDeploymentName", ollamaModelName);
        options.EnvironmentVariables.Add("CrestApps__AI__Providers__Ollama__Connections__Default__Endpoint", "http://localhost:11434");
        options.EnvironmentVariables.Add("CrestApps__AI__Providers__Ollama__Connections__Default__ChatDeploymentName", ollamaModelName);
        options.EnvironmentVariables.Add("CrestApps__BlazorApp__MCP__Server__AuthenticationType", "None");
        options.EnvironmentVariables.Add("CrestApps__BlazorApp__A2A__Host__AuthenticationType", "None");
        options.EnvironmentVariables.Add("CrestApps__BlazorApp__A2A__Host__ExposeAgentsAsSkill", "true");

        // Prevent VS-injected startup hooks (BrowserRefresh, DeltaApplier, BrowserLink)
        // from loading into child processes. These middlewares can interfere with
        // multipart file uploads when running under VS + Aspire.
        options.EnvironmentVariables["DOTNET_STARTUP_HOOKS"] = "";
        options.EnvironmentVariables["__ASPNETCORE_BROWSER_TOOLS"] = "";
    });

builder.AddProject<Projects.CrestApps_Core_Mvc_Samples_McpClient>("McpClientSample")
    .WithReference(mvcWeb)
    .WithReference(blazorWeb)
    .WaitFor(mvcWeb)
    .WaitFor(blazorWeb)
    .WithHttpsEndpoint(5002, name: "HttpsMvcMcpClient")
    .WithEnvironment("Mcp__DefaultServer", "MvcWeb")
    .WithEnvironment("Mcp__Servers__MvcWeb__DisplayName", "MVC Web")
    .WithEnvironment("Mcp__Servers__MvcWeb__Endpoint", "https://localhost:5001/mcp")
    .WithEnvironment("Mcp__Servers__BlazorWeb__DisplayName", "Blazor Web")
    .WithEnvironment("Mcp__Servers__BlazorWeb__Endpoint", "https://localhost:5201/mcp");

builder.AddProject<Projects.CrestApps_Core_Mvc_Samples_A2AClient>("A2AClientSample")
    .WithReference(mvcWeb)
    .WithReference(blazorWeb)
    .WaitFor(mvcWeb)
    .WaitFor(blazorWeb)
    .WithHttpsEndpoint(5003, name: "HttpsMvcA2AClient")
    .WithEnvironment("A2A__DefaultServer", "MvcWeb")
    .WithEnvironment("A2A__Servers__MvcWeb__DisplayName", "MVC Web")
    .WithEnvironment("A2A__Servers__MvcWeb__Endpoint", "https://localhost:5001")
    .WithEnvironment("A2A__Servers__BlazorWeb__DisplayName", "Blazor Web")
    .WithEnvironment("A2A__Servers__BlazorWeb__Endpoint", "https://localhost:5201");

var app = builder.Build();

// Open the Aspire Dashboard in the default browser after the app starts.
// We do this from code instead of using launchBrowser:true in launchSettings because
// VS attaches a browser management connection to browsers it launches, and that
// connection crashes when a native file dialog (e.g. file upload picker) opens.
// Opening the browser from code means VS has no management link to it.
var dashboardUrl = builder.Configuration["ASPNETCORE_URLS"]?.Split(';')
    .FirstOrDefault(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    ?? builder.Configuration["ASPNETCORE_URLS"]?.Split(';').FirstOrDefault();

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() =>
{
    if (!string.IsNullOrEmpty(dashboardUrl))
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dashboardUrl,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Non-critical: browser open is best-effort.
        }
    }
});

try
{
    await app.RunAsync();
}
catch (Exception ex)
{
    WriteCrashEntry("Distributed application terminated unexpectedly", ex);

    throw;
}

static bool IsBenignAppHostException(Exception exception)
{
    return exception switch
    {
        AggregateException aggregateException => aggregateException.InnerExceptions.All(IsBenignAppHostException),
        OperationCanceledException => true,
        IOException ioException when ioException.Message.Contains("request was aborted", StringComparison.OrdinalIgnoreCase) => true,
        _ => false,
    };
}
