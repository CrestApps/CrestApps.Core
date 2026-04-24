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
using CrestApps.Core.AI.Ollama;
using CrestApps.Core.AI.OpenAI;
using CrestApps.Core.AI.OpenAI.Azure;
using CrestApps.Core.Azure.AISearch;
using CrestApps.Core.Blazor.Web;
using CrestApps.Core.Blazor.Web.Areas.AIChat.BackgroundServices;
using CrestApps.Core.Blazor.Web.Areas.AIChat.Endpoints;
using CrestApps.Core.Blazor.Web.Areas.AIChat.Hubs;
using CrestApps.Core.Blazor.Web.Areas.AIChat.Services;
using CrestApps.Core.Blazor.Web.Areas.ChatInteractions.Hubs;
using CrestApps.Core.Blazor.Web.Components;
using CrestApps.Core.Blazor.Web.Services;
using CrestApps.Core.Blazor.Web.Tools;
using CrestApps.Core.Data.EntityCore;
using CrestApps.Core.Elasticsearch;
using CrestApps.Core.SignalR;
using CrestApps.Core.Startup.Shared.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

// =============================================================================
// CrestApps AI Framework — Blazor Example Application
// =============================================================================
// This sample keeps the Blazor host setup small, moves shared sample-host
// plumbing behind extensions, and leaves the CrestApps feature registrations
// easy to scan in one place.
//
// Sections:
//   1. Shared sample-host defaults
//   2. Blazor host setup
//   3. CrestApps framework composition
//   4. MCP and custom tools
//   5. Background tasks and pipeline
// =============================================================================
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = SampleHostContentRootResolver.ResolveContentRoot("CrestApps.Core.Blazor.Web.csproj"),
});

// Shared sample-host defaults: HostOptions, NLog, App_Data, App_Data/appsettings.json,
// and the SiteSettingsStore + option bridges that feed the sample admin UI.
var appDataPath = builder.AddSharedSampleHostDefaults();

// =============================================================================
// 2. BLAZOR HOST SETUP
// =============================================================================
builder.Services.AddLocalization();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpContextAccessor();

// =============================================================================
// 5. AUTHENTICATION & AUTHORIZATION
// =============================================================================
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/account/access-denied";
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", policy => policy.RequireRole("Administrator"));
builder.Services.AddCascadingAuthenticationState();

// =============================================================================
// 3. CRESTAPPS FRAMEWORK COMPOSITION
// =============================================================================
builder.Services
    .AddBlazorSampleHostServices(appDataPath)
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
            .AddReferenceDownloads()
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

// =============================================================================
// 4. MCP AND CUSTOM TOOLS
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
builder.Services.AddHostedService<AIChatSessionCloseBackgroundService>();
builder.Services.AddSingleton<SampleAIChatDocumentIndexingQueue>();
builder.Services.AddSingleton<ISampleAIChatDocumentIndexingQueue>(sp => sp.GetRequiredService<SampleAIChatDocumentIndexingQueue>());
builder.Services.AddHostedService<AIChatDocumentIndexingBackgroundService>();

var app = builder.Build();

// Initialize the sample backing store before serving requests.
await app.Services.InitializeEntityCoreSchemaAsync();

// Seed the article records shown throughout the sample UI.
await app.Services.SeedArticlesAsync();
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

app.MapAuthEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
