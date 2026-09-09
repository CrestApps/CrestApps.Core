using System.Text.Json;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Tests.Framework.AI;

/// <summary>
/// Covers the move of realtime from a stored chat mode plus a separate deployment field onto the chat
/// deployment's own capability.
/// </summary>
/// <remarks>
/// A profile used to answer "is this a voice conversation?" twice — once as <c>ChatMode.Realtime</c> and
/// once by naming a <c>RealtimeDeploymentName</c> — and the two could disagree with the deployment actually
/// selected. Both are gone. These tests pin the upgrade path for profiles written before that change.
/// </remarks>
public sealed class RealtimeCapabilityMigrationTests
{
    [Fact]
    public void Deserialize_WhenProfileNamedASeparateRealtimeDeployment_ShouldBecomeTheChatDeployment()
    {
        // Arrange. The realtime deployment is the model the profile actually converses with.
        var stored = """
            {
              "Name": "voice-assistant",
              "ChatDeploymentName": "gpt-4o",
              "RealtimeDeploymentName": "gpt-realtime"
            }
            """;

        // Act
        var profile = JsonSerializer.Deserialize<AIProfile>(stored, ExtensibleEntityExtensions.JsonSerializerOptions);

        // Assert
        Assert.Equal("gpt-realtime", profile.ChatDeploymentName);
    }

    [Fact]
    public void Deserialize_WhenProfileNamedASeparateRealtimeDeployment_ShouldKeepTheOldChatModelForBackgroundWork()
    {
        // Arrange. A realtime profile could never answer a typed message, so its chat deployment was only
        // ever reachable as the fallback for summarization, title generation, and data extraction. Losing it
        // would silently move that work onto an unrelated model.
        var stored = """
            {
              "Name": "voice-assistant",
              "ChatDeploymentName": "gpt-4o",
              "RealtimeDeploymentName": "gpt-realtime"
            }
            """;

        // Act
        var profile = JsonSerializer.Deserialize<AIProfile>(stored, ExtensibleEntityExtensions.JsonSerializerOptions);

        // Assert
        Assert.Equal("gpt-4o", profile.UtilityDeploymentName);
    }

    [Fact]
    public void Deserialize_WhenProfileAlreadyNamesAUtilityDeployment_ShouldNotOverwriteIt()
    {
        // Arrange. An explicit utility choice outranks the inferred one.
        var stored = """
            {
              "Name": "voice-assistant",
              "ChatDeploymentName": "gpt-4o",
              "UtilityDeploymentName": "gpt-4o-mini",
              "RealtimeDeploymentName": "gpt-realtime"
            }
            """;

        // Act
        var profile = JsonSerializer.Deserialize<AIProfile>(stored, ExtensibleEntityExtensions.JsonSerializerOptions);

        // Assert
        Assert.Equal("gpt-realtime", profile.ChatDeploymentName);
        Assert.Equal("gpt-4o-mini", profile.UtilityDeploymentName);
    }

    [Fact]
    public void Deserialize_WhenProfileHasNoRealtimeDeployment_ShouldLeaveTheChatDeploymentAlone()
    {
        // Arrange
        var stored = """
            {
              "Name": "text-assistant",
              "ChatDeploymentName": "gpt-4o"
            }
            """;

        // Act
        var profile = JsonSerializer.Deserialize<AIProfile>(stored, ExtensibleEntityExtensions.JsonSerializerOptions);

        // Assert
        Assert.Equal("gpt-4o", profile.ChatDeploymentName);
        Assert.Null(profile.UtilityDeploymentName);
    }

    [Fact]
    public void Deserialize_WhenProfileIsSavedAgain_ShouldNotWriteBackTheLegacyField()
    {
        // Arrange. The migration is a one-time translation. Once saved, nothing re-applies it.
        var stored = """
            {
              "Name": "voice-assistant",
              "ChatDeploymentName": "gpt-4o",
              "RealtimeDeploymentName": "gpt-realtime"
            }
            """;

        var profile = JsonSerializer.Deserialize<AIProfile>(stored, ExtensibleEntityExtensions.JsonSerializerOptions);

        // Act
        var persisted = JsonSerializer.Serialize(profile, ExtensibleEntityExtensions.JsonSerializerOptions);
        var reloaded = JsonSerializer.Deserialize<AIProfile>(persisted, ExtensibleEntityExtensions.JsonSerializerOptions);

        // Assert
        Assert.DoesNotContain("RealtimeDeploymentName", persisted, StringComparison.Ordinal);
        Assert.Equal("gpt-realtime", reloaded.ChatDeploymentName);
        Assert.Equal("gpt-4o", reloaded.UtilityDeploymentName);
    }

    [Theory]
    [InlineData("\"Realtime\"")]
    [InlineData("3")]
    [InlineData("\"SomethingElse\"")]
    public void Deserialize_WhenSettingsDeclareARemovedChatMode_ShouldFallBackToTextInputRatherThanThrow(string storedMode)
    {
        // Arrange. Removing an enum member is only safe because the converter tolerates the old value.
        // Without it the whole settings object would fail to deserialize and take the profile with it.
        var stored = $$"""
            {
              "ChatMode": {{storedMode}},
              "VoiceName": "cedar"
            }
            """;

        // Act
        var settings = JsonSerializer.Deserialize<ChatModeProfileSettings>(stored, ExtensibleEntityExtensions.JsonSerializerOptions);

        // Assert
        Assert.Equal(ChatMode.TextInput, settings.ChatMode);
        Assert.Equal("cedar", settings.VoiceName);
    }

    [Theory]
    [InlineData(ChatMode.TextInput)]
    [InlineData(ChatMode.AudioInput)]
    [InlineData(ChatMode.Conversation)]
    public void Deserialize_WhenSettingsDeclareASupportedChatMode_ShouldRoundTrip(ChatMode mode)
    {
        // Arrange
        var stored = $$"""
            { "ChatMode": "{{mode}}" }
            """;

        // Act
        var settings = JsonSerializer.Deserialize<ChatModeProfileSettings>(stored, ExtensibleEntityExtensions.JsonSerializerOptions);

        // Assert
        Assert.Equal(mode, settings.ChatMode);
    }
}
