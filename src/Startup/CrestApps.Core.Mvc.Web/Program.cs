using CrestApps.Core;
using CrestApps.Core.AI;
using CrestApps.Core.AI.A2A;
using CrestApps.Core.AI.Azure.AISearch;
using CrestApps.Core.AI.AzureAIInference;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Claude;
using CrestApps.Core.AI.Copilot;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Endpoints;
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
using CrestApps.Core.Azure.AISearch;
using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Elasticsearch;
using CrestApps.Core.Mvc.Web.Areas.AIChat.Endpoints;
using CrestApps.Core.Mvc.Web.Areas.AIChat.BackgroundServices;
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
using Microsoft.Extensions.Options;

// =============================================================================
// CrestApps AI Framework — MVC Example Application
// =============================================================================
// This sample keeps the normal ASP.NET Core MVC setup small, moves reusable
// sample-host plumbing behind extensions, and leaves the CrestApps feature
// registrations easy to scan in one place.
//
// Sections:
//   1. Shared sample-host defaults
//   2. ASP.NET Core MVC host setup
//   3. CrestApps framework composition
//   4. MCP and custom tools
//   5. Background tasks and pipeline
// =============================================================================
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = SampleHostContentRootResolver.ResolveContentRoot("CrestApps.Core.Mvc.Web.csproj"),
});

// Shared sample-host defaults: HostOptions, NLog, App_Data, App_Data/appsettings.json,
// and the SiteSettingsStore + option bridges that feed the sample admin UI.
var appDataPath = builder.AddSharedSampleHostDefaults();

// =============================================================================
// 2. ASP.NET CORE MVC SETUP
// =============================================================================
// This is the regular MVC host setup. Everything after this block is optional
// CrestApps composition or sample-only infrastructure.
// =============================================================================
builder.Services.AddLocalization();
builder.Services.AddControllersWithViews(options =>
    {
        options.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build()));
    })
    .AddRazorRuntimeCompilation()
    .AddCrestAppsStoreCommitterFilter();

builder.Services.AddHttpContextAccessor();

// =============================================================================
// 3. CRESTAPPS FRAMEWORK COMPOSITION
// =============================================================================
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", policy => policy.RequireRole("Administrator"));

builder.Services.Configure<AIProviderConnectionCatalogOptions>(options =>
{
    options.ConnectionSections.Clear();
    options.ConnectionSections.Add("CrestApps:AI:Connections");

    options.ProviderSections.Clear();
});

builder.Services
    .AddMvcSampleHostServices(appDataPath)
    .AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddYesSqlStores()
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
// 4. MCP AND CUSTOM TOOLS
// =============================================================================
// The sample exposes the same CrestApps handlers over MCP and adds two simple
// demo tools to show how host-specific tools plug into the framework.
_ = builder.Services.AddMcpServer(options =>
{
    options.ServerInfo = new()
    {
        Name = "CrestApps MVC MCP Server",
        Version = "1.0",
    };
}).WithHttpTransport()
.WithCrestAppsHandlers();

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
// 5. BACKGROUND TASKS AND PIPELINE
// =============================================================================
// These hosted services keep chat sessions, document indexing, and data-source
// synchronization moving in the background. Keep only the workers your app needs.
// =============================================================================
builder.Services.AddHostedService<AIChatSessionCloseBackgroundService>();
builder.Services.AddSingleton<SampleAIChatDocumentIndexingQueue>();
builder.Services.AddSingleton<ISampleAIChatDocumentIndexingQueue>(sp => sp.GetRequiredService<SampleAIChatDocumentIndexingQueue>());
builder.Services.AddHostedService<AIChatDocumentIndexingBackgroundService>();

var app = builder.Build();

// Initialize the sample backing store before serving requests.
await app.Services.InitializeYesSqlSchemaAsync();

// Seed the article records shown throughout the sample UI.
await app.Services.SeedArticlesAsync();
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
        var siteSettings = context.RequestServices.GetRequiredService<SiteSettingsStore>();
        var settings = siteSettings.TryGet<McpServerOptions>(out var storedSettings)
            ? storedSettings
            : context.RequestServices.GetRequiredService<IOptions<McpServerOptions>>().Value;

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
