using CrestApps.Core;
using CrestApps.Core.AI;
using CrestApps.Core.AI.A2A;
using CrestApps.Core.AI.Azure.AISearch;
using CrestApps.Core.AI.AzureAIInference;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Claude;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Copilot;
using CrestApps.Core.AI.Copilot.Services;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Endpoints;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.OpenXml;
using CrestApps.Core.AI.Documents.Pdf;
using CrestApps.Core.AI.Elasticsearch;
using CrestApps.Core.AI.Indexing;
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
using CrestApps.Core.Blazor.Web;
using CrestApps.Core.Blazor.Web.Areas.Admin.Handlers;
using CrestApps.Core.Blazor.Web.Areas.AI.Handlers;
using CrestApps.Core.Blazor.Web.Areas.AI.Services;
using CrestApps.Core.Blazor.Web.Areas.AIChat.BackgroundServices;
using CrestApps.Core.Blazor.Web.Areas.AIChat.Endpoints;
using CrestApps.Core.Blazor.Web.Areas.AIChat.Handlers;
using CrestApps.Core.Blazor.Web.Areas.AIChat.Hubs;
using CrestApps.Core.Blazor.Web.Areas.AIChat.Services;
using CrestApps.Core.Blazor.Web.Areas.ChatInteractions.Hubs;
using CrestApps.Core.Blazor.Web.Areas.DataSources.BackgroundServices;
using CrestApps.Core.Blazor.Web.Areas.DataSources.Handlers;
using CrestApps.Core.Blazor.Web.Areas.DataSources.Services;
using CrestApps.Core.Blazor.Web.Areas.Indexing.Services;
using CrestApps.Core.Blazor.Web.Components;
using CrestApps.Core.Blazor.Web.Services;
using CrestApps.Core.Blazor.Web.Tools;
using CrestApps.Core.Data.EntityCore;
using CrestApps.Core.Data.EntityCore.Services;
using CrestApps.Core.Elasticsearch;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Services;
using CrestApps.Core.SignalR;
using CrestApps.Core.Startup.Shared.Models;
using CrestApps.Core.Startup.Shared.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NLog.Web;

// =============================================================================
// CrestApps AI Framework — Blazor Example Application
// =============================================================================
// This Program.cs demonstrates how to bootstrap a Blazor Server application
// using the CrestApps AI Framework. It mirrors the MVC sample but uses
// Blazor Interactive Server rendering and EntityCore (EF Core) stores instead
// of YesSql.
//
// Sections:
//   1. Crash Diagnostics & Host Resilience
//   2. Logging
//   3. Application Configuration (App_Data appsettings override)
//   4. Blazor Server setup
//   5. Authentication & Authorization
//   6. CrestApps foundation + AI services (EntityCore stores)
//   7. Elasticsearch services
//   8. Azure AI Search services
//   9. MCP — Model Context Protocol (client + server)
//  10. Custom AI Tools
//  11. Data Store (EntityCore / SQLite)
//  12. Background Tasks
//  13. Middleware Pipeline
// =============================================================================
var builder = WebApplication.CreateBuilder(args);

// Prevent background/hosted-service exceptions from tearing down the host.
builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

// =============================================================================
// 2. LOGGING
// =============================================================================
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.WebHost.UseNLog();
var appDataPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(appDataPath);

// =============================================================================
// 3. APPLICATION CONFIGURATION
// =============================================================================
builder.Configuration.AddJsonFile("App_Data/appsettings.json", optional: true, reloadOnChange: false);

builder.Services.AddSingleton(new SiteSettingsStore(appDataPath));

builder.Services.AddSingleton<IConfigureOptions<GeneralAIOptions>, SiteSettingsConfigureGeneralAIOptions>();
builder.Services.AddSingleton<IConfigureOptions<AIMemoryOptions>, SiteSettingsConfigureAIMemoryOptions>();
builder.Services.AddSingleton<IConfigureOptions<InteractionDocumentOptions>, SiteSettingsConfigureInteractionDocumentOptions>();
builder.Services.AddSingleton<IConfigureOptions<AIDataSourceOptions>, SiteSettingsConfigureAIDataSourceOptions>();
builder.Services.AddSingleton<IConfigureOptions<ChatInteractionMemoryOptions>, SiteSettingsConfigureChatInteractionMemoryOptions>();
builder.Services.AddSingleton<IConfigureOptions<DefaultAIDeploymentSettings>, SiteSettingsConfigureDefaultDeploymentOptions>();

// =============================================================================
// 4. BLAZOR SERVER SETUP
// =============================================================================
builder.Services.AddLocalization();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpContextAccessor();

// =============================================================================
// 5. AUTHENTICATION & AUTHORIZATION
// =============================================================================
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/account/login";
    options.AccessDeniedPath = "/account/access-denied";
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", policy => policy.RequireRole("Administrator"));
builder.Services.AddCascadingAuthenticationState();

// =============================================================================
// 6. CRESTAPPS FOUNDATION + AI SERVICES (EntityCore stores)
// =============================================================================
var dbPath = Path.Combine(appDataPath, "crestapps-blazor.db");
builder.Services
    .AddCoreEntityCoreSqliteDataStore($"Data Source={dbPath}")
    .AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddEntityCoreStores()
        .AddMarkdown()
        .AddClaudeOrchestrator()
        .AddCopilotOrchestrator()
        .AddChatInteractions(chatInteractions => chatInteractions
            .AddEntityCoreStores()
            .ConfigureChatHubOptions<ChatInteractionHub>()
         )
        .AddDocumentProcessing(documentProcessing => documentProcessing
            .AddEntityCoreStores()
            .AddOpenXml()
            .AddPdf()
        )
        .AddAIMemory(memory => memory
            .AddEntityCoreStores()
        )
        .AddA2AClient(a2a => a2a
            .AddEntityCoreStores()
        )
        .AddMcpClient(mcp => mcp
            .AddEntityCoreStores()
        )
        .AddMcpServer(mcpServer => mcpServer
            .AddEntityCoreStores()
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
        .AddEntityCoreStores()
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

// Articles support — same as MVC.
builder.Services.AddDocumentCatalog<Article, DocumentCatalog<Article>>();
builder.Services.AddSharedArticleServices();
builder.Services.AddKeyedScoped<IAIReferenceLinkResolver, ArticleAIReferenceLinkResolver>(IndexProfileTypes.Articles);
builder.Services.AddScoped<MvcCitationReferenceCollector>();
builder.Services.AddScoped<CompositeAIReferenceLinkResolver>();
builder.Services.AddScoped<IAIDataSourceIndexingService, DefaultAIDataSourceIndexingService>();
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionSettingsHandler, DocumentChatInteractionSettingsHandler>());

// Host-specific managers.
builder.Services.AddScoped<DefaultAIProfileTemplateManager>();
builder.Services.AddScoped<IAIProfileTemplateManager>(sp => sp.GetRequiredService<DefaultAIProfileTemplateManager>());
builder.Services.AddScoped<INamedSourceCatalogManager<AIProfileTemplate>>(sp => sp.GetRequiredService<DefaultAIProfileTemplateManager>());
builder.Services.AddScoped<INamedCatalogManager<AIProfileTemplate>>(sp => sp.GetRequiredService<DefaultAIProfileTemplateManager>());
builder.Services.AddScoped<DefaultAIDeploymentManager>();
builder.Services.AddScoped<IAIDeploymentManager>(sp => sp.GetRequiredService<DefaultAIDeploymentManager>());
builder.Services.AddScoped<INamedSourceCatalogManager<AIDeployment>>(sp => sp.GetRequiredService<DefaultAIDeploymentManager>());
builder.Services.AddScoped<IAIProfileManager, SimpleAIProfileManager>();
builder.Services.AddScoped<AIProfileDocumentService>();
builder.Services.AddScoped<AIProfileTemplateDocumentService>();

// Host-specific chat session services.
builder.Services.AddScoped<MvcAIChatSessionEventService>();
builder.Services.AddScoped<MvcAICompletionUsageService>();
builder.Services.AddScoped<MvcAIChatSessionEventPostCloseObserver>();
builder.Services.AddScoped<MvcAIChatSessionExtractedDataService>();
builder.Services.AddScoped<IAICompletionUsageObserver>(sp => sp.GetRequiredService<MvcAICompletionUsageService>());
builder.Services.AddScoped<IAIChatSessionAnalyticsRecorder>(sp => sp.GetRequiredService<MvcAIChatSessionEventPostCloseObserver>());
builder.Services.AddScoped<IAIChatSessionConversionGoalRecorder>(sp => sp.GetRequiredService<MvcAIChatSessionEventPostCloseObserver>());
builder.Services.AddScoped<IAIChatSessionExtractedDataRecorder>(sp => sp.GetRequiredService<MvcAIChatSessionExtractedDataService>());
builder.Services.AddScoped<IAIChatSessionHandler, AnalyticsChatSessionHandler>();

// Host-specific indexing and authorization services.
builder.Services.AddScoped<ICatalogEntryHandler<AIMemoryEntry>, AIMemoryEntryIndexingHandler>();
builder.Services.AddScoped<MvcAIDocumentIndexingService>();
builder.Services.AddScoped<ISearchIndexProfileManager, SearchIndexProfileManager>();
builder.Services.AddScoped<IAuthorizationHandler, MvcChatInteractionDocumentAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, MvcAIChatSessionDocumentAuthorizationHandler>();
builder.Services.AddScoped<IAIChatDocumentEventHandler, MvcAIChatDocumentEventHandler>();

// Host-specific data-source and article services.
builder.Services.AddScoped<ICatalogEntryHandler<AIDataSource>, AIDataSourceIndexingHandler>();
builder.Services.AddScoped<ICatalogEntryHandler<Article>, ArticleIndexingHandler>();
builder.Services.Configure<IndexProfileSourceOptions>(options => options
    .AddOrUpdate(ElasticsearchConstants.ProviderName, "Elasticsearch", IndexProfileTypes.Articles, descriptor =>
    {
        descriptor.DisplayName = "Articles";
        descriptor.Description = "Create an Elasticsearch index for sample article records managed in the Blazor app.";
    })
);

builder.Services.Configure<IndexProfileSourceOptions>(options => options
    .AddOrUpdate(ElasticsearchConstants.ProviderName, "Azure AI Search", IndexProfileTypes.Articles, descriptor =>
    {
        descriptor.DisplayName = "Articles";
        descriptor.Description = "Create an Azure AI Search index for sample article records managed in the Blazor app.";
    })
);

// =============================================================================
// 9. MCP — MODEL CONTEXT PROTOCOL
// =============================================================================
_ = builder.Services.AddMcpServer(options =>
{
    options.ServerInfo = new()
    {
        Name = "CrestApps Blazor MCP Server",
        Version = "1.0",
    };
}).WithHttpTransport()
.WithCrestAppsHandlers();

// =============================================================================
// 10. CUSTOM AI TOOLS
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
// 11. DATA STORE — EntityCore with SQLite
// =============================================================================
builder.Services.AddScoped<ICopilotCredentialStore, JsonFileCopilotCredentialStore>();
builder.Services.ConfigureOptions<MvcCopilotOptionsConfiguration>();
builder.Services.ConfigureOptions<MvcClaudeOptionsConfiguration>();

// =============================================================================
// 12. BACKGROUND TASKS
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

// EntityCore schema initialization — creates tables on first run.
await app.Services.InitializeEntityCoreSchemaAsync();

// Seed sample articles on first run.
await app.Services.SeedArticlesAsync();


// =============================================================================
// 13. MIDDLEWARE PIPELINE
// =============================================================================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error").UseHsts();
}

app.UseHttpsRedirection()
    .UseStaticFiles()
    .UseRouting()
    .UseAuthentication()
    .UseAuthorization()
    .UseAntiforgery();

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

app.MapAuthEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
