using CrestApps.Core;
using CrestApps.Core.AI.Claude.Models;
using CrestApps.Core.AI.Claude.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;

namespace CrestApps.OrchardCore.Tests.Framework.Mvc;

public sealed class AIProfileViewModelClaudeTests
{
    [Fact]
    public void FromProfile_WhenAnthropicMetadataExists_ShouldPopulateAnthropicFields()
    {
        var profile = new AIProfile();
        profile.Put(new ClaudeSessionMetadata
        {
            ClaudeModel = "claude-sonnet-4-6",
        });

        var model = AIProfileViewModel.FromProfile(profile);

        Assert.Equal("claude-sonnet-4-6", model.ClaudeModel);
    }

    [Fact]
    public void ApplyTo_WhenOrchestratorIsAnthropic_ShouldPersistAnthropicMetadata()
    {
        var model = new AIProfileViewModel
        {
            Name = "profile",
            DisplayText = "Profile",
            Type = AIProfileType.Chat,
            Source = "source",
            OrchestratorName = ClaudeOrchestrator.OrchestratorName,
            ClaudeModel = "claude-opus-4-1",
            SelectedToolNames = [],
            SelectedAgentNames = [],
            SelectedA2AConnectionIds = [],
            SelectedMcpConnectionIds = [],
            PromptTemplates = [],
            DataExtractionEntries = [],
            PostSessionTasks = [],
            ConversionGoals = [],
        };

        var profile = new AIProfile();

        model.ApplyTo(profile);

        Assert.True(profile.TryGet<ClaudeSessionMetadata>(out var metadata));
        Assert.Equal("claude-opus-4-1", metadata.ClaudeModel);
    }

    [Fact]
    public void ApplyTo_WhenOrchestratorIsNotAnthropic_ShouldRemoveExistingAnthropicMetadata()
    {
        var model = new AIProfileViewModel
        {
            Name = "profile",
            DisplayText = "Profile",
            Type = AIProfileType.Chat,
            Source = "source",
            OrchestratorName = "default",
            SelectedToolNames = [],
            SelectedAgentNames = [],
            SelectedA2AConnectionIds = [],
            SelectedMcpConnectionIds = [],
            PromptTemplates = [],
            DataExtractionEntries = [],
            PostSessionTasks = [],
            ConversionGoals = [],
        };

        var profile = new AIProfile();
        profile.Put(new ClaudeSessionMetadata
        {
            ClaudeModel = "claude-sonnet-4-6",
        });

        model.ApplyTo(profile);

        Assert.False(profile.TryGet<ClaudeSessionMetadata>(out _));
    }
}
