using CrestApps.Core;
using CrestApps.Core.AI;
using CrestApps.Core.AI.A2A;
using CrestApps.Core.AI.AzureAIInference;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Chat.Endpoints;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Copilot;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Ftp;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Markdown;
using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Ollama;
using CrestApps.Core.AI.OpenAI;
using CrestApps.Core.AI.OpenAI.Azure;
using CrestApps.Core.AI.OpenXml;
using CrestApps.Core.AI.Pdf;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Sftp;
using CrestApps.Core.Azure.AISearch;
using CrestApps.Core.Blazor.Web.Areas.Admin.Handlers;
using CrestApps.Core.Blazor.Web.Areas.Admin.Models;
using CrestApps.Core.Blazor.Web.Areas.Admin.Services;
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
using CrestApps.Core.Blazor.Web.Services;
using CrestApps.Core.Blazor.Web.Tools;
using CrestApps.Core.Data.EntityCore;
using CrestApps.Core.Elasticsearch;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Services;
using CrestApps.Core.SignalR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NLog.Web;

// =============================================================================
// CrestApps AI Framework — Blazor Server Example Application
// =============================================================================
// This Program.cs demonstrates how to bootstrap a Blazor Server application
// using the CrestApps AI Framework with EntityCore (EF Core) data stores.
// It mirrors every feature of the MVC example but uses Blazor components
// instead of MVC controllers/views and EntityCore instead of YesSql.
// =============================================================================
var builder = WebApplication.CreateBuilder(args);

// Early startup marker.
var crashLogDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "logs");
Directory.CreateDirectory(crashLogDir);
File.WriteAllText(
    Path.Combine(crashLogDir, "startup-marker.txt"),
    $"Process started at {DateTime.UtcNow:O}, PID={Environment.ProcessId}{Environment.NewLine}");

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    var message = $"[{DateTime.UtcNow:O}] Unhandled exception (IsTerminating={e.IsTerminating}):{Environment.NewLine}{e.ExceptionObject}{Environment.NewLine}";
    try { File.AppendAllText(Path.Combine(crashLogDir, "crash.log"), message); } catch { }
    Console.Error.Write(message);
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    var message = $"[{DateTime.UtcNow:O}] Unobserved task exception:{Environment.NewLine}{e.Exception}{Environment.NewLine}";
    try { File.AppendAllText(Path.Combine(crashLogDir, "crash.log"), message); } catch { }
    Console.Error.Write(message);
    e.SetObserved();
};

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

// =============================================================================
// 4. BLAZOR SERVER SETUP
// =============================================================================
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();

// =============================================================================
// 5. AUTHENTICATION & AUTHORIZATION
// =============================================================================
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", policy => policy.RequireRole("Administrator"));

// =============================================================================
// 6. CRESTAPPS FOUNDATION + AI SERVICES (EntityCore stores)
// =============================================================================
var entityCoreDbPath = Path.Combine(appDataPath, "blazor-app.db");

// Blazor-specific DbContext for articles and analytics (separate DB to avoid EnsureCreated conflicts).
var blazorAppDbPath = Path.Combine(appDataPath, "blazor-app-data.db");
builder.Services.AddDbContext<BlazorAppDbContext>(options => options.UseSqlite($"Data Source={blazorAppDbPath}"));
builder.Services.AddScoped<ArticleService>();
builder.Services.AddScoped<ICatalog<Article>, ArticleCatalog>();

builder.Services
    .AddCoreEntityCoreSqliteDataStore($"Data Source={entityCoreDbPath}")
    .AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddEntityCoreStores()
        .AddMarkdown()
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

// Localization (required by IStringLocalizer<T> used in AI handlers).
builder.Services.AddLocalization();

// Core managers not registered by the generic EntityCore store setup.
builder.Services.AddScoped<IAIProfileManager, SimpleAIProfileManager>();
builder.Services.AddScoped<ISearchIndexProfileManager, SearchIndexProfileManager>();

// Host-specific managers (mirrors MVC YesSql registrations).
builder.Services
    .AddScoped<DefaultAIProfileTemplateManager>()
    .AddScoped<IAIProfileTemplateManager>(sp => sp.GetRequiredService<DefaultAIProfileTemplateManager>())
    .AddScoped<INamedSourceCatalogManager<AIProfileTemplate>>(sp => sp.GetRequiredService<DefaultAIProfileTemplateManager>())
    .AddScoped<INamedCatalogManager<AIProfileTemplate>>(sp => sp.GetRequiredService<DefaultAIProfileTemplateManager>())
    .AddScoped<DefaultAIDeploymentManager>()
    .AddScoped<IAIDeploymentManager>(sp => sp.GetRequiredService<DefaultAIDeploymentManager>())
    .AddScoped<INamedSourceCatalogManager<AIDeployment>>(sp => sp.GetRequiredService<DefaultAIDeploymentManager>())
    .AddScoped<AIProfileDocumentService>()
    .AddScoped<AIProfileTemplateDocumentService>();

// Host-specific chat session services.
builder.Services
    .AddScoped<MvcAIChatSessionEventService>()
    .AddScoped<MvcAICompletionUsageService>()
    .AddScoped<MvcAIChatSessionEventPostCloseObserver>()
    .AddScoped<MvcAIChatSessionExtractedDataService>()
    .AddScoped<IAICompletionUsageObserver>(sp => sp.GetRequiredService<MvcAICompletionUsageService>())
    .AddScoped<IAIChatSessionAnalyticsRecorder>(sp => sp.GetRequiredService<MvcAIChatSessionEventPostCloseObserver>())
    .AddScoped<IAIChatSessionConversionGoalRecorder>(sp => sp.GetRequiredService<MvcAIChatSessionEventPostCloseObserver>())
    .AddScoped<IAIChatSessionExtractedDataRecorder>(sp => sp.GetRequiredService<MvcAIChatSessionExtractedDataService>())
    .AddScoped<IAIChatSessionHandler, AnalyticsChatSessionHandler>();

// Host-specific indexing and authorization services.
builder.Services
    .AddScoped<ICatalogEntryHandler<AIMemoryEntry>, AIMemoryEntryIndexingHandler>()
    .AddScoped<MvcAIDocumentIndexingService>()
    .AddScoped<IAuthorizationHandler, MvcChatInteractionDocumentAuthorizationHandler>()
    .AddScoped<IAuthorizationHandler, MvcAIChatSessionDocumentAuthorizationHandler>()
    .AddScoped<IAIChatDocumentEventHandler, MvcAIChatDocumentEventHandler>();

// Host-specific data-source and article catalog entry handlers.
builder.Services
    .AddScoped<ICatalogEntryHandler<AIDataSource>, AIDataSourceIndexingHandler>()
    .AddScoped<ICatalogEntryHandler<Article>, ArticleIndexingHandler>();

// Articles support.
builder.Services.AddScoped<ArticleIndexingService>();
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IIndexProfileHandler, ArticleIndexProfileHandler>());
builder.Services.AddKeyedScoped<IAIReferenceLinkResolver, ArticleAIReferenceLinkResolver>(IndexProfileTypes.Articles);
builder.Services.AddScoped<MvcCitationReferenceCollector>();
builder.Services.AddScoped<CompositeAIReferenceLinkResolver>();
builder.Services.AddScoped<IAIDataSourceIndexingService, DefaultAIDataSourceIndexingService>();
builder.Services.Configure<IndexProfileSourceOptions>(options => options.AddOrUpdate(CrestApps.Core.Elasticsearch.ServiceCollectionExtensions.ProviderName, "Elasticsearch", IndexProfileTypes.Articles, descriptor =>
{
    descriptor.DisplayName = "Articles";
    descriptor.Description = "Create an Elasticsearch index for sample article records managed in the Blazor app.";
}));
builder.Services.Configure<IndexProfileSourceOptions>(options => options.AddOrUpdate(CrestApps.Core.Azure.AISearch.ServiceCollectionExtensions.ProviderName, "Azure AI Search", IndexProfileTypes.Articles, descriptor =>
{
    descriptor.DisplayName = "Articles";
    descriptor.Description = "Create an Azure AI Search index for sample article records managed in the Blazor app.";
}));

// =============================================================================
// 10. MCP — MODEL CONTEXT PROTOCOL
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
// 11. CUSTOM AI TOOLS
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
// 12. FILE STORE & COPILOT
// =============================================================================
builder.Services.AddSingleton(new FileSystemFileStore(Path.Combine(appDataPath, "Documents")));
builder.Services.AddScoped<ICopilotCredentialStore, JsonFileCopilotCredentialStore>();
builder.Services.ConfigureOptions<MvcCopilotOptionsConfiguration>();

// =============================================================================
// 13. BACKGROUND TASKS
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

// MVC controllers for Account login/logout (non-Blazor form post).
builder.Services.AddControllersWithViews();

var app = builder.Build();

try
{
    // EntityCore schema initialization — creates tables on first run.
    await app.Services.InitializeEntityCoreSchemaAsync();

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

app.MapControllers();
app.MapRazorComponents<CrestApps.Core.Blazor.Web.Components.App>()
    .AddInteractiveServerRenderMode();

try
{
    await app.RunAsync();
}
catch (Exception ex)
{
    var crashMessage = $"[{DateTime.UtcNow:O}] Host terminated unexpectedly:{Environment.NewLine}{ex}{Environment.NewLine}";
    try { File.AppendAllText(Path.Combine(crashLogDir, "crash.log"), crashMessage); } catch { }
    Console.Error.Write(crashMessage);
    throw;
}
