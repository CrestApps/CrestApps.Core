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
using CrestApps.Core.Mvc.Web.Areas.AIChat.BackgroundServices;
using CrestApps.Core.Mvc.Web.Areas.AIChat.Endpoints;
using CrestApps.Core.Mvc.Web.Areas.AIChat.Hubs;
using CrestApps.Core.Mvc.Web.Areas.AIChat.Services;
using CrestApps.Core.Mvc.Web.Areas.ChatInteractions.Hubs;
using CrestApps.Core.Mvc.Web.Services;
using CrestApps.Core.Mvc.Web.Tools;
using CrestApps.Core.SignalR;
using CrestApps.Core.Startup.Shared.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NLog.Web;

// =============================================================================
// CrestApps AI Framework — MVC Example Application
// =============================================================================
// This sample shows one way to compose CrestApps.Core into an ASP.NET Core MVC
// host. Keep the ASP.NET Core plumbing your app needs, then add only the
// CrestApps features and backing stores that match your own project.
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

// Keep the sample host alive if a background service throws. In production you
// may prefer StopHost if a failed background worker should bring the app down.
builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

// =============================================================================
// 2. LOGGING
// =============================================================================
// The sample uses NLog and writes to App_Data/logs. Swap this for any logging
// provider you already standardize on.
// =============================================================================
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.WebHost.UseNLog();
var appDataPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(appDataPath);

// =============================================================================
// 3. APPLICATION CONFIGURATION
// =============================================================================
// The sample keeps long-lived infrastructure settings in App_Data/appsettings.json
// and lets the admin UI manage mutable settings through App_Data/site-settings.json.
// WebApplicationBuilder already loads the root appsettings files, so only add the
// App_Data override here.
// =============================================================================
builder.Configuration.AddJsonFile("App_Data/appsettings.json", optional: true, reloadOnChange: false);

// SiteSettingsStore owns App_Data/site-settings.json and persists admin-edited
// values outside the normal IConfiguration pipeline.
builder.Services.AddSingleton(new SiteSettingsStore(appDataPath));

// Bridge admin-managed site settings into the framework options that CrestApps
// services consume.
builder.Services.AddSingleton<IConfigureOptions<GeneralAIOptions>, SiteSettingsConfigureGeneralAIOptions>();
builder.Services.AddSingleton<IConfigureOptions<AIMemoryOptions>, SiteSettingsConfigureAIMemoryOptions>();
builder.Services.AddSingleton<IConfigureOptions<InteractionDocumentOptions>, SiteSettingsConfigureInteractionDocumentOptions>();
builder.Services.AddSingleton<IConfigureOptions<AIDataSourceOptions>, SiteSettingsConfigureAIDataSourceOptions>();
builder.Services.AddSingleton<IConfigureOptions<ChatInteractionMemoryOptions>, SiteSettingsConfigureChatInteractionMemoryOptions>();
builder.Services.AddSingleton<IConfigureOptions<DefaultAIDeploymentSettings>, SiteSettingsConfigureDefaultDeploymentOptions>();

// =============================================================================
// 4. ASP.NET CORE MVC SETUP
// =============================================================================
// Register the normal MVC host services first. Everything below this point is
// optional framework composition that you can trim for your own app.
// =============================================================================
builder.Services.AddLocalization();
builder.Services.AddControllersWithViews(options =>
    {
        options.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build()));
    })
    .AddCrestAppsStoreCommitterFilter();

builder.Services.AddHttpContextAccessor();

// =============================================================================
// 5. AUTHENTICATION & AUTHORIZATION
// =============================================================================
// The sample uses cookie auth plus a simple Admin policy. Replace this block
// with your own authentication/authorization strategy.
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
// Add the CrestApps core runtime, then opt into only the AI features you need.
// For your own host, keep the store implementation and remove unused feature
// registrations instead of copying the whole sample blindly.
// =============================================================================
builder.Services
    .AddCoreYesSqlDataStore(appDataPath)
    .AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddYesSqlStores()
        // Add provider-specific options only if you manage providers from configuration.
        // Each additional feature below is optional.
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
            .AddReferenceDownloads()
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
// Provider registrations live in the AddAISuite block above. Keep only the
// providers your app actually uses and supply their credentials in configuration.
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
builder.Services
    .AddSharedArticleServices()
    .AddSharedTemplateProviders();
builder.Services.AddKeyedScoped<IAIReferenceLinkResolver, ArticleAIReferenceLinkResolver>(IndexProfileTypes.Articles);
builder.Services.AddScoped<MvcCitationReferenceCollector>();
builder.Services.AddScoped<CompositeAIReferenceLinkResolver>();
builder.Services.AddScoped<IAIDataSourceIndexingService, DefaultAIDataSourceIndexingService>();
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionSettingsHandler, DocumentChatInteractionSettingsHandler>());
builder.Services.Configure<IndexProfileSourceOptions>(options => options
    .AddOrUpdate(ElasticsearchConstants.ProviderName, "Elasticsearch", IndexProfileTypes.Articles, descriptor =>
    {
        descriptor.DisplayName = "Articles";
        descriptor.Description = "Create an Elasticsearch index for sample article records managed in the MVC app.";
    })
);

// =============================================================================
// 9. AZURE AI SEARCH SERVICES
// =============================================================================
// Article registrations below exist to demonstrate document indexing and source
// references in the sample UI.
// =============================================================================
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
// This exposes the CrestApps tool, prompt, and resource handlers over an MCP
// endpoint. Remove it if your host does not need to serve MCP.
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
// Register app-specific tools here. Selectable tools appear in the admin UI;
// non-selectable tools stay internal to your orchestrator pipeline.
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
// CrestApps.Core is store-agnostic. This sample uses YesSql with SQLite, but
// you can swap in Entity Framework Core or your own implementations as long as
// the required catalog/store abstractions are satisfied.
// =============================================================================
// Provider-specific sample services.
builder.Services.AddScoped<ICopilotCredentialStore, JsonFileCopilotCredentialStore>();
builder.Services.ConfigureOptions<MvcCopilotOptionsConfiguration>();
builder.Services.ConfigureOptions<MvcClaudeOptionsConfiguration>();

// =============================================================================
// 13. BACKGROUND TASKS
// =============================================================================
// These hosted services keep chat sessions, document indexing, and data-source
// synchronization moving in the background. Keep only the workers your app needs.
// =============================================================================
builder.Services.AddHostedService<AIChatSessionCloseBackgroundService>();
builder.Services.AddSingleton<MvcAIChatDocumentIndexingQueue>();
builder.Services.AddSingleton<IMvcAIChatDocumentIndexingQueue>(sp => sp.GetRequiredService<MvcAIChatDocumentIndexingQueue>());
builder.Services.AddHostedService<AIChatDocumentIndexingBackgroundService>();

var app = builder.Build();

// Initialize the backing store before serving requests.
await app.Services.InitializeYesSqlSchemaAsync();

// Seed sample content used by the demo UI.
await app.Services.SeedArticlesAsync();

// =============================================================================
// 14. MIDDLEWARE PIPELINE
// =============================================================================
// Standard ASP.NET Core middleware first, then the CrestApps endpoints and hubs.
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
    .AddDownloadAIDocumentEndpoint()
    .AddUploadChatInteractionDocumentEndpoint()
    .AddRemoveChatInteractionDocumentEndpoint()
    .AddUploadChatSessionDocumentEndpoint()
    .AddRemoveChatSessionDocumentEndpoint();

app.MapControllerRoute(name: "areas", pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

await app.RunAsync();
