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
}
