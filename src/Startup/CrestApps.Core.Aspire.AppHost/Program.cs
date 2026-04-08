using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
    Console.Error.WriteLine("[AppHost] Unhandled exception terminated the process.");

    if (eventArgs.ExceptionObject is Exception exception)
    {
        Console.Error.WriteLine(exception);
    }
};

TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
{
    Console.Error.WriteLine("[AppHost] Unobserved task exception.");
    Console.Error.WriteLine(eventArgs.Exception);
};

AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    Console.Error.WriteLine("[AppHost] Process exit signaled.");
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
    });

builder.AddProject<Projects.CrestApps_Core_Mvc_Samples_McpClient>("MvcMcpClientSample")
    .WithReference(mvcWeb)
    .WaitFor(mvcWeb)
    .WithHttpsEndpoint(5002, name: "HttpsMvcMcpClient")
    .WithEnvironment("Mcp__Endpoint", "https://localhost:5001/mcp/sse");

builder.AddProject<Projects.CrestApps_Core_Mvc_Samples_A2AClient>("MvcA2AClientSample")
    .WithReference(mvcWeb)
    .WaitFor(mvcWeb)
    .WithHttpsEndpoint(5003, name: "HttpsMvcA2AClient")
    .WithEnvironment("A2A__Endpoint", "https://localhost:5001");

var app = builder.Build();

try
{
    await app.RunAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine("[AppHost] Distributed application terminated unexpectedly.");
    Console.Error.WriteLine(ex);
    throw;
}
