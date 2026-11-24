using CrestApps.Core.AI.Models;
using CrestApps.Core.Blazor.Web.ViewModels;

namespace CrestApps.Core.Tests.Framework.Blazor;

public sealed class BlazorAIProfileViewModelTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RunToolsWithoutAsking_RoundTripsThroughTheProfile(bool runWithoutAsking)
    {
        var profile = new AIProfile();

        new AIProfileViewModel { RunToolsWithoutAsking = runWithoutAsking }.ApplyTo(profile);

        Assert.Equal(runWithoutAsking, profile.TryGetSettings<AIToolApprovalSettings>(out var settings) && settings.RunsAllWithoutAsking());
        Assert.Equal(runWithoutAsking, AIProfileViewModel.FromProfile(profile).RunToolsWithoutAsking);
    }

    [Fact]
    public void FromProfile_ProfileSavedBeforeTheSetting_AsksForApproval()
    {
        Assert.False(AIProfileViewModel.FromProfile(new AIProfile()).RunToolsWithoutAsking);
    }

    [Theory]
    [InlineData("Create.razor")]
    [InlineData("Edit.razor")]
    public void ToolApprovalSelect_HasTheOptionValuesBlazorWritesForABool(string page)
    {
        // InputSelect<bool> writes its value as "true" or "false". With options "True" and "False", no option
        // matched, so the select showed blank on Create and when a saved profile was opened again.
        var content = File.ReadAllText(GetRepositoryPath("src", "Startup", "CrestApps.Core.Blazor.Web", "Components", "Pages", "AI", "AIProfiles", page));
        var start = content.IndexOf("@bind-Value=\"_model.RunToolsWithoutAsking\"", StringComparison.Ordinal);

        Assert.True(start >= 0, $"{page} has no Tool approval select.");

        var select = content[start..content.IndexOf("</InputSelect>", start, StringComparison.Ordinal)];

        Assert.Contains("<option value=\"false\">", select, StringComparison.Ordinal);
        Assert.Contains("<option value=\"true\">", select, StringComparison.Ordinal);
    }

    private static string GetRepositoryPath(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeSegments]);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find '{Path.Combine(relativeSegments)}' from '{AppContext.BaseDirectory}'.");
    }
}
