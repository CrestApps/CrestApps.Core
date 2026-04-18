using CrestApps.Core.AI.Claude.Models;
using CrestApps.Core.Mvc.Web.Areas.AIChat.Services;

namespace CrestApps.Core.Tests.Framework.Chat.Claude;

public sealed class MvcClaudeSettingsExtensionsTests
{
    [Fact]
    public void IsConfigured_WhenSettingsMissingApiKey_ShouldReturnFalse()
    {
        var settings = new ClaudeSettings
        {
            AuthenticationType = ClaudeAuthenticationType.ApiKey,
            DefaultModel = "claude-sonnet-4-6",
        };

        Assert.False(settings.IsConfigured());
    }

    [Fact]
    public void IsConfigured_WhenSettingsHasApiKeyAndDefaultModel_ShouldReturnTrue()
    {
        var settings = new ClaudeSettings
        {
            AuthenticationType = ClaudeAuthenticationType.ApiKey,
            ProtectedApiKey = "protected-api-key",
            DefaultModel = "claude-sonnet-4-6",
        };

        Assert.True(settings.IsConfigured());
    }

    [Fact]
    public void IsConfigured_WhenSettingsAreNotConfigured_ShouldReturnFalse()
    {
        var settings = new ClaudeSettings
        {
            AuthenticationType = ClaudeAuthenticationType.NotConfigured,
            ProtectedApiKey = "protected-api-key",
        };

        Assert.False(settings.IsConfigured());
    }

    [Fact]
    public void IsConfigured_WhenOptionsMissingDefaultModel_ShouldReturnFalse()
    {
        var options = new ClaudeOptions
        {
            ApiKey = null,
        };

        Assert.False(options.IsConfigured());
    }

    [Fact]
    public void IsConfigured_WhenOptionsHasApiKey_ShouldReturnTrue()
    {
        var options = new ClaudeOptions
        {
            ApiKey = "api-key",
        };

        Assert.True(options.IsConfigured());
    }
}
