using CrestApps.Core;
using CrestApps.Core.AI;
using CrestApps.Core.AI.A2A;
using CrestApps.Core.AI.Azure.AISearch;
using CrestApps.Core.AI.AzureAIInference;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Claude;
using CrestApps.Core.AI.Copilot;
using CrestApps.Core.AI.Copilot.Services;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Endpoints;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.OpenXml;
using CrestApps.Core.AI.Documents.Pdf;
using CrestApps.Core.AI.Elasticsearch;
using CrestApps.Core.AI.Markdown;
using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Mcp.Ftp;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Mcp.Sftp;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Ollama;
using CrestApps.Core.AI.OpenAI;
using CrestApps.Core.AI.OpenAI.Azure;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Azure.AISearch;
using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Elasticsearch;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Mvc.Web.Areas.Admin.Handlers;
using CrestApps.Core.Mvc.Web.Areas.AIChat.BackgroundServices;
using CrestApps.Core.Mvc.Web.Areas.AIChat.Endpoints;
using CrestApps.Core.Mvc.Web.Areas.AIChat.Hubs;
using CrestApps.Core.Mvc.Web.Areas.AIChat.Services;
using CrestApps.Core.Mvc.Web.Areas.ChatInteractions.Hubs;
using CrestApps.Core.Mvc.Web.Areas.DataSources.BackgroundServices;
using CrestApps.Core.Mvc.Web.Areas.DataSources.Services;
using CrestApps.Core.Mvc.Web.Services;
using CrestApps.Core.Mvc.Web.Tools;
using CrestApps.Core.SignalR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NLog.Web;

// =============================================================================
// CrestApps AI Framework — MVC Example Application
// =============================================================================
// This Program.cs demonstrates how to bootstrap an ASP.NET Core MVC application
// using the CrestApps AI Framework. It shows each feature registration step in
// the order they should be applied, with comments explaining what each extension
// does and why it is needed.
//
// Sections:
//   1. Crash Diagnostics & Host Resilience
//   2. Logging
//   3. Application Configuration (App_Data appsettings override)
//   4. ASP.NET Core MVC setup
//   5. Authentication & Authorization
//   6. CrestApps foundation + AI services
//   7. AI Clients (OpenAI, Azure OpenAI, Ollama, Azure AI Inference)
//   8. Elasticsearch services
//   9. Azure AI Search services
//  10. MCP — Model Context Protocol (client + server)
//  11. Custom AI Tools
//  12. Data Store (YesSql / SQLite — replaceable with any ORM)
//  13. Background Tasks
//  14. Middleware Pipeline
// =============================================================================
var builder = WebApplication.CreateBuilder(args);

// Early startup marker — writes immediately to confirm the process launched.
var crashLogDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "logs");
Directory.CreateDirectory(crashLogDir);
File.WriteAllText(
    Path.Combine(crashLogDir, "startup-marker.txt"),
    $"Process started at {DateTime.UtcNow:O}, PID={Environment.ProcessId}{Environment.NewLine}");

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    var message = $"[{DateTime.UtcNow:O}] Unhandled exception (IsTerminating={e.IsTerminating}):{Environment.NewLine}{e.ExceptionObject}{Environment.NewLine}";

    try
    {
        File.AppendAllText(Path.Combine(crashLogDir, "crash.log"), message);
    }
    catch
    {
        // Best-effort — the process is already dying.
    }

    Console.Error.Write(message);
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    var message = $"[{DateTime.UtcNow:O}] Unobserved task exception:{Environment.NewLine}{e.Exception}{Environment.NewLine}";

    try
    {
        File.AppendAllText(Path.Combine(crashLogDir, "crash.log"), message);
    }
    catch
    {
        // Best-effort.
    }

    Console.Error.Write(message);
    e.SetObserved();
};

// Prevent background/hosted-service exceptions from tearing down the host.
// The default BackgroundServiceExceptionBehavior.StopHost silently kills the
// process when any IHostedService.ExecuteAsync throws — even if the exception
// is transient. With Ignore, the exception is still logged but the host
// continues running.
builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});
// =============================================================================
// 2. LOGGING
// =============================================================================
// NLog writes daily log files to App_Data/logs/. Replace with your preferred
// logging provider (Serilog, Application Insights, etc.) if desired.
// =============================================================================
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.WebHost.UseNLog();
var appDataPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(appDataPath);
// =============================================================================
// 3. APPLICATION CONFIGURATION
// =============================================================================
// Two-layer App_Data configuration:
//
// App_Data/appsettings.json holds infrastructure config (AI connections,
// credentials, Elasticsearch, Azure AI Search, etc.) that is rarely changed at
// runtime. reloadOnChange is disabled to avoid FileSystemWatcher race
// conditions in the App_Data directory. Restart the app after editing.
//
// App_Data/site-settings.json holds mutable admin-managed settings (general AI
// options, default deployments, chat interaction, admin widget, etc.) owned
// entirely by SiteSettingsStore. It is NOT registered in the configuration
// pipeline — SiteSettingsStore loads it into memory at startup and writes it
// back atomically via SaveChangesAsync().
//
// NOTE: The default appsettings.json and appsettings.{env}.json files are
// already registered by WebApplicationBuilder — do not re-add them here.
// =============================================================================
builder.Configuration.AddJsonFile("App_Data/appsettings.json", optional: true, reloadOnChange: false);

// SiteSettingsStore is the single owner of App_Data/site-settings.json.
// It migrates old JSON keys on load and serves reads from memory.
builder.Services.AddSingleton(new SiteSettingsStore(appDataPath));

// Bridge SiteSettingsStore → framework IOptions<T> so that framework services
// (orchestrators, memory, data sources, etc.) see the admin-managed values.
builder.Services.AddSingleton<IConfigureOptions<GeneralAIOptions>, SiteSettingsConfigureGeneralAIOptions>();
builder.Services.AddSingleton<IConfigureOptions<AIMemoryOptions>, SiteSettingsConfigureAIMemoryOptions>();
builder.Services.AddSingleton<IConfigureOptions<InteractionDocumentOptions>, SiteSettingsConfigureInteractionDocumentOptions>();
builder.Services.AddSingleton<IConfigureOptions<AIDataSourceOptions>, SiteSettingsConfigureAIDataSourceOptions>();
builder.Services.AddSingleton<IConfigureOptions<ChatInteractionMemoryOptions>, SiteSettingsConfigureChatInteractionMemoryOptions>();
builder.Services.AddSingleton<IConfigureOptions<DefaultAIDeploymentSettings>, SiteSettingsConfigureDefaultDeploymentOptions>();
// =============================================================================
// 4. ASP.NET CORE MVC SETUP
// =============================================================================
// Start with the standard ASP.NET Core building blocks before adding CrestApps-
// specific features. This keeps the host framework registrations easy to find.
// =============================================================================
builder.Services.AddLocalization();
builder.Services.AddControllersWithViews()
    .AddCrestAppsStoreCommitterFilter();
builder.Services.AddHttpContextAccessor();
// =============================================================================
// 5. AUTHENTICATION & AUTHORIZATION
// =============================================================================
// Cookie-based authentication with a simple "Admin" policy. Replace with your
// preferred auth scheme (JWT, OpenID Connect, etc.).
// =============================================================================
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", policy => policy.RequireRole("Administrator"));
// =============================================================================
// 6. CRESTAPPS FOUNDATION + AI SERVICES
// =============================================================================
// These are the shared CrestApps service registrations that sit on top of the
// normal ASP.NET Core host. Keep them together so consumers can clearly see the
// minimum CrestApps foundation, then remove optional features as needed.
// =============================================================================
builder.Services
    .AddCoreYesSqlDataStore(appDataPath)
    .AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddYesSqlStores()
        // .ConfigureProviderOptions(builder.Configuration.GetSection("CrestApps:AI:Providers"))
        // Optional AI features layered on top of the core AI + orchestration runtime.
        .AddMarkdown()
        .AddClaudeOrchestrator()
        .AddCopilotOrchestrator()
        .AddChatInteractions(chatInteractions => chatInteractions
            .AddYesSqlStores()
            .ConfigureChatHubOptions<ChatInteractionHub>()
         )
        .AddDocumentProcessing(documentProcessing => documentProcessing
            .AddYesSqlStores()
            .AddOpenXml()
            .AddPdf()
        )
        .AddAIMemory(memory => memory
            .AddYesSqlStores()
        )
        .AddA2AClient(a2a => a2a
            .AddYesSqlStores()
        )
        .AddMcpClient(mcp => mcp
            .AddYesSqlStores()
        )
        .AddMcpServer(mcpServer => mcpServer
            .AddYesSqlStores()
            .AddFtpResources()
            .AddSftpResources()
        )
        .AddSignalR(addStoreCommitterFilter: true)
        .AddA2AHost()
        .AddOpenAI()
        .AddAzureOpenAI()
        .AddOllama()
        .AddAzureAIInference()
    )
    .AddIndexingServices(indexing => indexing
        .AddYesSqlStores()
        .AddElasticsearch(builder.Configuration.GetSection("CrestApps:Elasticsearch"), elasticsearch => elasticsearch
            .AddAIDocuments()
            .AddAIDataSources()
            .AddAIMemory()
        )
        .AddAzureAISearch(builder.Configuration.GetSection("CrestApps:AzureAISearch"), azureAISearch => azureAISearch
            .AddAIDocuments()
            .AddAIDataSources()
            .AddAIMemory()
        )
     )
 );

// =============================================================================
// 7. AI PROVIDERS
// =============================================================================
// Register the AI completion providers you want to use. Each provider adds an
// IAICompletionClient implementation that knows how to communicate with its
// platform. Provider connection settings are read from appsettings.json under
// "CrestApps:AI:Providers". You only need to register the providers you use.
//
//   AddAISuite(ai => ai.AddOpenAI())          — OpenAI (api.openai.com)
//   AddAISuite(ai => ai.AddAzureOpenAI())     — Azure OpenAI Service
//   AddAISuite(ai => ai.AddOllama())          — Ollama (local/self-hosted models)
//   AddAISuite(ai => ai.AddAzureAIInference())— Azure AI Inference / GitHub Models
// =============================================================================
// =============================================================================
// 8. ELASTICSEARCH SERVICES
// =============================================================================
// Keep each vector-search backend in its own group so it is obvious which block
// to remove when the application does not use that provider.
// =============================================================================
// =============================================================================
// 9. AZURE AI SEARCH SERVICES
// =============================================================================
// This block mirrors the Elasticsearch group so each provider's registrations
// stay together and are easy to remove independently.
// =============================================================================
// Add Articles support to show document support example.
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IIndexProfileHandler, ArticleIndexProfileHandler>());
builder.Services.AddKeyedScoped<IAIReferenceLinkResolver, ArticleAIReferenceLinkResolver>(IndexProfileTypes.Articles);
builder.Services.AddScoped<MvcCitationReferenceCollector>();
builder.Services.AddScoped<CompositeAIReferenceLinkResolver>();
builder.Services.AddScoped<IAIDataSourceIndexingService, DefaultAIDataSourceIndexingService>();
builder.Services.Configure<IndexProfileSourceOptions>(options => options
    .AddOrUpdate(ElasticsearchConstants.ProviderName, "Elasticsearch", IndexProfileTypes.Articles, descriptor =>
    {
        descriptor.DisplayName = "Articles";
        descriptor.Description = "Create an Elasticsearch index for sample article records managed in the MVC app.";
    })
);

builder.Services.Configure<IndexProfileSourceOptions>(options => options
    .AddOrUpdate(ElasticsearchConstants.ProviderName, "Azure AI Search", IndexProfileTypes.Articles, descriptor =>
    {
        descriptor.DisplayName = "Articles";
        descriptor.Description = "Create an Azure AI Search index for sample article records managed in the MVC app.";
    })
);

// =============================================================================
// 10. MCP — MODEL CONTEXT PROTOCOL
// =============================================================================
// MCP server endpoint configuration (using the ModelContextProtocol SDK).
// This wires the CrestApps tool registry, prompt service, and resource service
// into the MCP protocol handlers served at the /mcp endpoint.
_ = builder.Services.AddMcpServer(options =>
{
    options.ServerInfo = new()
    {
        Name = "CrestApps MVC MCP Server",
        Version = "1.0",
    };
}).WithHttpTransport()
.WithCrestAppsHandlers();

// =============================================================================
// 11. CUSTOM AI TOOLS
// =============================================================================
// Register application-specific AI tools using the fluent builder pattern.
// Tools marked as Selectable() are visible in the UI for user assignment to
// profiles; system tools (no Selectable call) are used automatically by the
// orchestrator based on their Purpose.
// =============================================================================
builder.Services.AddCoreAITool<CalculatorTool>(CalculatorTool.TheName)
    .WithTitle("Calculator")
    .WithDescription("Performs basic arithmetic: add, subtract, multiply, or divide two numbers.")
    .WithCategory("Utilities")
    .Selectable();

builder.Services.AddCoreAITool<SendEmailTool>(SendEmailTool.TheName)
    .WithTitle("Send email")
    .WithDescription("Logs an email request with the supplied recipient, subject, and message.")
    .WithCategory("Communications")
    .Selectable();

// =============================================================================
// 12. DATA STORE — YesSql with SQLite
// =============================================================================
// The framework does not impose a specific data store. You must provide
// implementations of the store interfaces (IAIProfileManager,
// IAIChatSessionManager, IAIChatSessionPromptStore, IAIDocumentStore, etc.).
//
// This sample uses YesSql with SQLite. CrestApps.Core also ships the
// CrestApps.Core.Data.EntityCore package for EF Core-based stores, and hosts
// can provide their own implementations as long as they satisfy the same
// store/catalog abstractions.
// =============================================================================
// Copilot orchestrator: credential store and options configuration.
builder.Services.AddScoped<ICopilotCredentialStore, JsonFileCopilotCredentialStore>();
builder.Services.ConfigureOptions<MvcCopilotOptionsConfiguration>();
builder.Services.ConfigureOptions<MvcClaudeOptionsConfiguration>();

// =============================================================================
// 13. BACKGROUND TASKS
// =============================================================================
// These hosted services run periodic maintenance work. Implement your own
// IHostedService or use these as reference implementations.
// =============================================================================
builder.Services.AddHostedService<AIChatSessionCloseBackgroundService>();
builder.Services.AddSingleton<MvcAIChatDocumentIndexingQueue>();
builder.Services.AddSingleton<IMvcAIChatDocumentIndexingQueue>(sp => sp.GetRequiredService<MvcAIChatDocumentIndexingQueue>());
builder.Services.AddHostedService<AIChatDocumentIndexingBackgroundService>();
builder.Services.AddSingleton<MvcAIDataSourceIndexingQueue>();
builder.Services.AddSingleton<IMvcAIDataSourceIndexingQueue>(sp => sp.GetRequiredService<MvcAIDataSourceIndexingQueue>());
builder.Services.AddHostedService<AIDataSourceIndexingBackgroundService>();
builder.Services.AddHostedService<DataSourceSyncBackgroundService>();
builder.Services.AddHostedService<DataSourceAlignmentBackgroundService>();

var app = builder.Build();

try
{
    // YesSql schema initialization — creates tables on first run.
    await app.Services.InitializeYesSqlSchemaAsync();

    // Seed sample articles on first run.
    await app.Services.SeedArticlesAsync();
}
catch (Exception ex)
{
    var msg = $"[{DateTime.UtcNow:O}] Startup initialization failed:{Environment.NewLine}{ex}{Environment.NewLine}";

    try { File.AppendAllText(Path.Combine(crashLogDir, "crash.log"), msg); } catch { }

    Console.Error.Write(msg);

    throw;
}

// =============================================================================
// 14. MIDDLEWARE PIPELINE
// =============================================================================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error").UseHsts();
}

app.UseHttpsRedirection()
    .UseStaticFiles()
    .UseRouting()
    .UseAuthentication()
    .UseAuthorization();

app.UseWhen(context => context.Request.Path.StartsWithSegments("/mcp"), branch =>
{
    branch.Use(async (context, next) =>
    {
        var settings = context.RequestServices.GetRequiredService<SiteSettingsStore>().Get<McpServerOptions>();
        if (settings.AuthenticationType == McpServerAuthenticationType.None)
        {
            await next();
            return;
        }

        if (settings.AuthenticationType == McpServerAuthenticationType.ApiKey)
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            var providedKey = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization["Bearer ".Length..] : authorization.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase) ? authorization["ApiKey ".Length..] : authorization;
            if (!string.IsNullOrEmpty(settings.ApiKey) && string.Equals(providedKey, settings.ApiKey, StringComparison.Ordinal))
            {
                await next();
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            await context.ChallengeAsync();
            return;
        }

        if (settings.RequireAccessPermission && !context.User.IsInRole("Administrator"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next();
    });
});
app.MapHub<AIChatHub>("/hubs/ai-chat");
app.MapHub<ChatInteractionHub>("/hubs/chat-interaction");
app.MapMcp("mcp");
app.MapA2AHost();
app.AddChatApiEndpoints()
    .AddUploadChatInteractionDocumentEndpoint()
    .AddRemoveChatInteractionDocumentEndpoint()
    .AddUploadChatSessionDocumentEndpoint()
    .AddRemoveChatSessionDocumentEndpoint();

app.MapControllerRoute(name: "areas", pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

try
{
    await app.RunAsync();
}
catch (Exception ex)
{
    var crashMessage = $"[{DateTime.UtcNow:O}] Host terminated unexpectedly:{Environment.NewLine}{ex}{Environment.NewLine}";

    try
    {
        File.AppendAllText(Path.Combine(crashLogDir, "crash.log"), crashMessage);
    }
    catch
    {
        // Best-effort.
    }

    Console.Error.Write(crashMessage);

    throw;
}
