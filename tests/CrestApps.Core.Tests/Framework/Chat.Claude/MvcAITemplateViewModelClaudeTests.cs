using CrestApps.Core.AI;
using CrestApps.Core.AI.Claude.Models;
using CrestApps.Core.AI.Claude.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;

namespace CrestApps.Core.Tests.Framework.Chat.Claude;

public sealed class MvcAITemplateViewModelClaudeTests
{
    [Fact]
    public void FromTemplate_WhenAnthropicMetadataExists_ShouldPopulateAnthropicFields()
    {
        var template = new AIProfileTemplate
        {
            Source = AITemplateSources.Profile,
        };

        template.Put(new ClaudeSessionMetadata
        {
            ClaudeModel = "claude-sonnet-4-6",
        });

        var model = AITemplateViewModel.FromTemplate(template);

        Assert.Equal("claude-sonnet-4-6", model.ClaudeModel);
    }

    [Fact]
    public void ApplyTo_WhenOrchestratorIsAnthropic_ShouldPersistAnthropicMetadata()
    {
        var model = new AITemplateViewModel
        {
            Name = "support-template",
            Source = AITemplateSources.Profile,
            OrchestratorName = ClaudeOrchestrator.OrchestratorName,
            ClaudeModel = "claude-opus-4-1",
        };

        var template = new AIProfileTemplate();

        model.ApplyTo(template);

        Assert.True(template.TryGet<ClaudeSessionMetadata>(out var metadata));
        Assert.Equal("claude-opus-4-1", metadata.ClaudeModel);
    }

    [Fact]
    public void ApplyTo_WhenOrchestratorIsNotAnthropic_ShouldRemoveExistingAnthropicMetadata()
    {
        var model = new AITemplateViewModel
        {
            Name = "support-template",
            Source = AITemplateSources.Profile,
            OrchestratorName = "default",
        };

        var template = new AIProfileTemplate();
        template.Put(new ClaudeSessionMetadata
        {
            ClaudeModel = "claude-sonnet-4-6",
        });

        model.ApplyTo(template);

        Assert.False(template.TryGet<ClaudeSessionMetadata>(out _));
    }
}
