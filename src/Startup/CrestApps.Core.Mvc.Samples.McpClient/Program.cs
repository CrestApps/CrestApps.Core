using CrestApps.Core.Mvc.Samples.McpClient.Services;
using CrestApps.Core.Startup.Shared.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<McpClientFactory>();
builder.Services.AddSingleton(sp => new SampleServerSelectionService(
    sp.GetRequiredService<IConfiguration>(),
    "Mcp",
    "CrestApps.SampleClients.Mcp.SelectedServer"));

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.MapRazorPages();
app.MapGet("/server-selection", (
    string server,
    string returnUrl,
    HttpContext context,
    SampleServerSelectionService selectionService) =>
{
    if (selectionService.TryGetServer(server, out _))
    {
        selectionService.SetCurrent(context, server);
    }

    return Results.Redirect(IsLocalReturnUrl(returnUrl) ? returnUrl : "/");
});

await app.RunAsync();

static bool IsLocalReturnUrl(string returnUrl)
{
    return !string.IsNullOrWhiteSpace(returnUrl) &&
        returnUrl[0] == '/' &&
        (returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\'));
}
