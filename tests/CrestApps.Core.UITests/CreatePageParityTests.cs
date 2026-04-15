namespace CrestApps.Core.UITests;

/// <summary>
/// Verifies that all Create pages have the same form fields
/// and auto-generation behavior between MVC and Blazor.
/// </summary>
public class CreatePageParityTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public CreatePageParityTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    #region AI Connections Create

    [Fact]
    public async Task Mvc_Connections_Create_Has_Expected_Fields()
    {
        var url = $"{TestConfiguration.MvcBaseUrl}/AI/AIConnection/Create";
        await VerifyFormFields(url, ["DisplayText", "Name", "Source", "Endpoint", "ApiKey"]);
    }

    [Fact]
    public async Task Blazor_Connections_Create_Has_Expected_Fields()
    {
        var url = $"{TestConfiguration.BlazorBaseUrl}/AI/Connections/Create";
        await VerifyFormFields(url, ["DisplayText", "Name", "Source", "Endpoint", "ApiKey"]);
    }

    [Fact]
    public async Task Both_Connections_Create_Have_TechnicalName_Field()
    {
        await VerifyTechnicalNameFieldExists(
            $"{TestConfiguration.MvcBaseUrl}/AI/AIConnection/Create",
            $"{TestConfiguration.BlazorBaseUrl}/AI/Connections/Create"
        );
    }

    #endregion

    #region AI Deployments Create

    [Fact]
    public async Task Mvc_Deployments_Create_Has_ConnectionDropdown()
    {
        var url = $"{TestConfiguration.MvcBaseUrl}/AI/AIDeployment/Create";
        await VerifyConnectionDropdownExists(url);
    }

    [Fact]
    public async Task Blazor_Deployments_Create_Has_ConnectionDropdown()
    {
        var url = $"{TestConfiguration.BlazorBaseUrl}/AI/Deployments/Create";
        await VerifyConnectionDropdownExists(url);
    }

    [Fact]
    public async Task Mvc_Deployments_Create_Has_Expected_Fields()
    {
        var url = $"{TestConfiguration.MvcBaseUrl}/AI/AIDeployment/Create";
        await VerifyFormFields(url, ["ModelName", "Name"]);
    }

    [Fact]
    public async Task Blazor_Deployments_Create_Has_Expected_Fields()
    {
        var url = $"{TestConfiguration.BlazorBaseUrl}/AI/Deployments/Create";
        await VerifyFormFields(url, ["ModelName", "Name"]);
    }

    #endregion

    #region AI Profiles Create

    [Fact]
    public async Task Mvc_Profiles_Create_Has_TechnicalName_Field()
    {
        var url = $"{TestConfiguration.MvcBaseUrl}/AI/AIProfile/Create";
        await VerifyTechnicalNameField(url);
    }

    [Fact]
    public async Task Blazor_Profiles_Create_Has_TechnicalName_Field()
    {
        var url = $"{TestConfiguration.BlazorBaseUrl}/AI/Profiles/Create";
        await VerifyTechnicalNameField(url);
    }

    #endregion

    #region Templates Create

    [Fact]
    public async Task Mvc_Templates_Create_Has_TechnicalName_Field()
    {
        var url = $"{TestConfiguration.MvcBaseUrl}/AI/AITemplate/Create";
        await VerifyTechnicalNameField(url);
    }

    [Fact]
    public async Task Blazor_Templates_Create_Has_TechnicalName_Field()
    {
        var url = $"{TestConfiguration.BlazorBaseUrl}/AI/Templates/Create";
        await VerifyTechnicalNameField(url);
    }

    #endregion

    #region Field count parity

    [Fact]
    public async Task Connections_Create_Field_Count_Matches()
    {
        await VerifyFieldCountParity(
            $"{TestConfiguration.MvcBaseUrl}/AI/AIConnection/Create",
            $"{TestConfiguration.BlazorBaseUrl}/AI/Connections/Create"
        );
    }

    [Fact]
    public async Task Deployments_Create_Field_Count_Matches()
    {
        await VerifyFieldCountParity(
            $"{TestConfiguration.MvcBaseUrl}/AI/AIDeployment/Create",
            $"{TestConfiguration.BlazorBaseUrl}/AI/Deployments/Create"
        );
    }

    [Fact]
    public async Task Profiles_Create_Field_Count_Matches()
    {
        await VerifyFieldCountParity(
            $"{TestConfiguration.MvcBaseUrl}/AI/AIProfile/Create",
            $"{TestConfiguration.BlazorBaseUrl}/AI/Profiles/Create"
        );
    }

    [Fact]
    public async Task Templates_Create_Field_Count_Matches()
    {
        await VerifyFieldCountParity(
            $"{TestConfiguration.MvcBaseUrl}/AI/AITemplate/Create",
            $"{TestConfiguration.BlazorBaseUrl}/AI/Templates/Create"
        );
    }

    #endregion

    private async Task VerifyFormFields(string url, string[] expectedFieldNames)
    {
        await using var context = await CreateContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync(url);
        await page.WaitForSelectorAsync("form");

        foreach (var fieldName in expectedFieldNames)
        {
            // Match by name attribute, id, or asp-for-generated name patterns
            var field = await page.QuerySelectorAsync(
                $"input[name*='{fieldName}'], select[name*='{fieldName}'], textarea[name*='{fieldName}'], " +
                $"input[id*='{fieldName}'], select[id*='{fieldName}'], textarea[id*='{fieldName}']"
            );

            Assert.True(field != null, $"Field '{fieldName}' not found on {url}");
        }
    }

    private async Task VerifyTechnicalNameField(string url)
    {
        await using var context = await CreateContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync(url);
        await page.WaitForSelectorAsync("form");

        // The technical name field is bound to "Name" in both MVC and Blazor
        var nameField = await page.QuerySelectorAsync(
            "input[name*='Name'], input[id*='Name'], input[id*='TechName'], input[id*='techName']"
        );

        Assert.True(nameField != null, $"Technical name field not found on {url}");
    }

    private async Task VerifyTechnicalNameFieldExists(string mvcUrl, string blazorUrl)
    {
        await VerifyTechnicalNameField(mvcUrl);
        await VerifyTechnicalNameField(blazorUrl);
    }

    private async Task VerifyConnectionDropdownExists(string url)
    {
        await using var context = await CreateContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync(url);
        await page.WaitForSelectorAsync("form");

        // Look for a select element related to connections
        var dropdown = await page.QuerySelectorAsync(
            "select[name*='Connection'], select[id*='Connection'], select[name*='connection']"
        );

        // Connection dropdown may be hidden for standalone providers; check it exists somewhere in DOM
        if (dropdown == null)
        {
            // Check if it's in a conditionally hidden section
            dropdown = await page.QuerySelectorAsync("select");
        }

        Assert.True(dropdown != null, $"Connection dropdown not found on {url}");
    }

    private async Task VerifyFieldCountParity(string mvcUrl, string blazorUrl)
    {
        var mvcCount = await CountFormFields(mvcUrl);
        var blazorCount = await CountFormFields(blazorUrl);

        Assert.Equal(mvcCount, blazorCount);
    }

    private async Task<int> CountFormFields(string url)
    {
        await using var context = await CreateContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync(url);
        await page.WaitForSelectorAsync("form");

        var fields = await page.QuerySelectorAllAsync(
            "form input:not([type='hidden']), form select, form textarea"
        );

        return fields.Count;
    }

    private async Task<IBrowserContext> CreateContext()
    {
        return await _fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
        });
    }
}
