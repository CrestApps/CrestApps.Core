using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using static CrestApps.Core.Tests.Framework.AI.FakeAIDeploymentCatalog;

namespace CrestApps.Core.Tests.Framework.AI;

/// <summary>
/// Covers the move of "is this a voice conversation?" from the chat deployment's capability onto the chat
/// mode, and the read-time fold that keeps profiles written before that move working.
/// </summary>
/// <remarks>
/// A realtime chat deployment used to turn the whole chat UI voice-only, because one field was both the text
/// model and the realtime model. Conversation mode now decides how a conversation is carried, the chat
/// deployment means only "the text model this profile talks to", and the conversation deployment names the
/// model that speaks.
/// </remarks>
public sealed class ConversationModeResolutionTests
{
    [Fact]
    public async Task ResolveConversationModeAsync_WhenTheChatDeploymentIsRealtime_ShouldFoldItOntoTheConversationDeployment()
    {
        // Arrange. A profile stored before the conversation deployment existed names its speech-to-speech model
        // as its chat deployment, and stores TextInput as its chat mode -- the editor hid the chat mode field
        // entirely once the deployment was realtime. Nothing in the stored JSON says the profile is a voice
        // profile; only the deployment's own capability does, which is why this fold cannot happen while
        // deserializing.
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("gpt-5", AIDeploymentFeatureNames.TextGeneration),
            CreateDeployment("gpt-realtime", AIDeploymentFeatureNames.Realtime));

        // Act
        var resolution = await manager.ResolveConversationModeAsync(
            ChatMode.TextInput,
            conversationDeploymentName: null,
            chatDeploymentName: "gpt-realtime",
            hasSpeechToText: false,
            hasTextToSpeech: false,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert. The fold has to move the mode as well as the deployment name. Moving only the name would
        // leave the profile resolving to TextInput, which is the one outcome the fold exists to prevent.
        Assert.True(resolution.RealtimeEnabled);
        Assert.True(resolution.FoldedFromChatDeployment);
        Assert.Equal(ChatMode.Conversation, resolution.ChatMode);
        Assert.Equal("gpt-realtime", resolution.RealtimeDeploymentName);
    }

    [Fact]
    public async Task ResolveConversationModeAsync_WhenTheChatDeploymentIsATextModel_ShouldNotFold()
    {
        // Arrange. The fold must not fire for an ordinary text profile that happens to share a site with a
        // realtime deployment.
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("gpt-5", AIDeploymentFeatureNames.TextGeneration),
            CreateDeployment("gpt-realtime", AIDeploymentFeatureNames.Realtime));

        // Act
        var resolution = await manager.ResolveConversationModeAsync(
            ChatMode.TextInput,
            conversationDeploymentName: null,
            chatDeploymentName: "gpt-5",
            hasSpeechToText: true,
            hasTextToSpeech: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(resolution.RealtimeEnabled);
        Assert.False(resolution.FoldedFromChatDeployment);
        Assert.Equal(ChatMode.TextInput, resolution.ChatMode);
    }

    [Fact]
    public async Task ResolveConversationModeAsync_WhenAConversationDeploymentIsNamed_ShouldNotFoldTheChatDeployment()
    {
        // Arrange. Once a profile has been saved in the new shape its own choice answers the question, even
        // though the chat deployment it carries forward may still be a realtime model.
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("legacy-realtime", AIDeploymentFeatureNames.Realtime),
            CreateDeployment("gpt-realtime", AIDeploymentFeatureNames.Realtime));

        // Act
        var resolution = await manager.ResolveConversationModeAsync(
            ChatMode.Conversation,
            conversationDeploymentName: "gpt-realtime",
            chatDeploymentName: "legacy-realtime",
            hasSpeechToText: false,
            hasTextToSpeech: false,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("gpt-realtime", resolution.RealtimeDeploymentName);
        Assert.False(resolution.FoldedFromChatDeployment);
    }

    [Fact]
    public async Task ResolveConversationModeAsync_WhenNoDeploymentIsNamed_ShouldUseTheSiteDefault()
    {
        // Arrange. Empty means "resolve the default", which is the whole reason the field is not written with
        // the resolved value.
        var settings = new DefaultAIDeploymentSettings
        {
            DefaultRealtimeDeploymentName = "site-realtime",
        };

        var manager = CreateManager(
            settings,
            CreateDeployment("decoy-realtime", AIDeploymentFeatureNames.Realtime),
            CreateDeployment("site-realtime", AIDeploymentFeatureNames.Realtime));

        // Act
        var resolution = await manager.ResolveConversationModeAsync(
            ChatMode.Conversation,
            conversationDeploymentName: null,
            chatDeploymentName: null,
            hasSpeechToText: false,
            hasTextToSpeech: false,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(resolution.RealtimeEnabled);
        Assert.Equal("site-realtime", resolution.RealtimeDeploymentName);
        Assert.Null(resolution.RequestedDeploymentName);
    }

    [Fact]
    public async Task ResolveConversationModeAsync_WhenTheNamedDeploymentIsNotRealtimeCapable_ShouldReportItRatherThanFallBack()
    {
        // Arrange. The realtime slot falls through to the site default when the name it is given does not
        // qualify, so a typo would otherwise move the conversation onto a model the user never chose and say
        // nothing about it.
        var settings = new DefaultAIDeploymentSettings
        {
            DefaultRealtimeDeploymentName = "site-realtime",
        };

        var manager = CreateManager(
            settings,
            CreateDeployment("gpt-5", AIDeploymentFeatureNames.TextGeneration),
            CreateDeployment("site-realtime", AIDeploymentFeatureNames.Realtime));

        // Act
        var resolution = await manager.ResolveConversationModeAsync(
            ChatMode.Conversation,
            conversationDeploymentName: "gpt-5",
            chatDeploymentName: null,
            hasSpeechToText: false,
            hasTextToSpeech: false,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(resolution.IsMisconfigured);
        Assert.False(resolution.RealtimeEnabled);
        Assert.Equal("gpt-5", resolution.RequestedDeploymentName);
    }

    [Fact]
    public async Task ResolveConversationModeAsync_WhenNothingRealtimeExists_ShouldFallBackToTheSpeechCascade()
    {
        // Arrange. Conversation mode without a realtime deployment is the client-driven speech-to-text plus
        // text-to-speech conversation, exactly as it has always been.
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("gpt-5", AIDeploymentFeatureNames.TextGeneration));

        // Act
        var resolution = await manager.ResolveConversationModeAsync(
            ChatMode.Conversation,
            conversationDeploymentName: null,
            chatDeploymentName: "gpt-5",
            hasSpeechToText: true,
            hasTextToSpeech: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(resolution.RealtimeEnabled);
        Assert.Equal(ChatMode.Conversation, resolution.ChatMode);
    }

    [Fact]
    public async Task ResolveConversationModeAsync_WhenRealtimeResolves_ShouldKeepConversationModeWithoutSpeechDeployments()
    {
        // Arrange. A speech-to-speech model needs neither a transcription nor a synthesis deployment, so the
        // cascade's requirements must not gate it.
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("gpt-realtime", AIDeploymentFeatureNames.Realtime));

        // Act
        var resolution = await manager.ResolveConversationModeAsync(
            ChatMode.Conversation,
            conversationDeploymentName: "gpt-realtime",
            chatDeploymentName: null,
            hasSpeechToText: false,
            hasTextToSpeech: false,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(resolution.RealtimeEnabled);
        Assert.Equal(ChatMode.Conversation, resolution.ChatMode);
    }

    [Fact]
    public async Task ResolveConversationModeAsync_WhenConversationHasOnlySpeechToText_ShouldDegradeToDictation()
    {
        // Arrange
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("gpt-5", AIDeploymentFeatureNames.TextGeneration));

        // Act
        var resolution = await manager.ResolveConversationModeAsync(
            ChatMode.Conversation,
            conversationDeploymentName: null,
            chatDeploymentName: "gpt-5",
            hasSpeechToText: true,
            hasTextToSpeech: false,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ChatMode.AudioInput, resolution.ChatMode);
        Assert.False(resolution.RealtimeEnabled);
    }

    [Theory]
    [InlineData(ChatMode.TextInput)]
    [InlineData(ChatMode.AudioInput)]
    public async Task ResolveConversationModeAsync_WhenTheModeIsNotConversation_ShouldNeverStartARealtimeSession(ChatMode configuredMode)
    {
        // Arrange. The presence of a realtime deployment is not, on its own, a reason to speak. That was the
        // old behavior this feature removes.
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("gpt-5", AIDeploymentFeatureNames.TextGeneration),
            CreateDeployment("gpt-realtime", AIDeploymentFeatureNames.Realtime));

        // Act
        var resolution = await manager.ResolveConversationModeAsync(
            configuredMode,
            conversationDeploymentName: "gpt-realtime",
            chatDeploymentName: "gpt-5",
            hasSpeechToText: true,
            hasTextToSpeech: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(resolution.RealtimeEnabled);
        Assert.Null(resolution.RealtimeDeploymentName);
    }

    [Fact]
    public async Task ResolveConversationModeAsync_WhenAnInteractionNamesAConversationDeployment_ShouldSpeakEvenWhileTheSiteIsTextOnly()
    {
        // Arrange. An interaction has no chat mode of its own -- the site holds it -- so naming a
        // speech-to-speech model on the interaction is the only way it can ask to speak. That is what choosing
        // such a model always did, and a site-wide text setting must not take it away.
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("gpt-5", AIDeploymentFeatureNames.TextGeneration),
            CreateDeployment("gpt-realtime", AIDeploymentFeatureNames.Realtime));

        // Act
        var resolution = await manager.ResolveConversationModeAsync(
            ChatMode.TextInput,
            conversationDeploymentName: "gpt-realtime",
            chatDeploymentName: "gpt-5",
            hasSpeechToText: false,
            hasTextToSpeech: false,
            chatModeIsSiteWide: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(resolution.RealtimeEnabled);
        Assert.Equal(ChatMode.Conversation, resolution.ChatMode);
    }

    [Fact]
    public async Task ResolveConversationModeAsync_WhenAMigratedInteractionIsSaved_ShouldKeepSpeaking()
    {
        // Arrange. The fold only fires while the conversation deployment is still empty. Saving the settings
        // panel writes the folded name, so without the site-wide rule above the interaction would lose its voice
        // the first time anything else about it was edited -- the regression this pins.
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("gpt-5", AIDeploymentFeatureNames.TextGeneration),
            CreateDeployment("gpt-realtime", AIDeploymentFeatureNames.Realtime));

        // Act
        var beforeSave = await manager.ResolveConversationModeAsync(
            ChatMode.TextInput,
            conversationDeploymentName: null,
            chatDeploymentName: "gpt-realtime",
            hasSpeechToText: false,
            hasTextToSpeech: false,
            chatModeIsSiteWide: true,
            cancellationToken: TestContext.Current.CancellationToken);

        var afterSave = await manager.ResolveConversationModeAsync(
            ChatMode.TextInput,
            conversationDeploymentName: beforeSave.RequestedDeploymentName,
            chatDeploymentName: null,
            hasSpeechToText: false,
            hasTextToSpeech: false,
            chatModeIsSiteWide: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(beforeSave.RealtimeEnabled);
        Assert.True(afterSave.RealtimeEnabled);
        Assert.Equal(beforeSave.ChatMode, afterSave.ChatMode);
        Assert.Equal(beforeSave.RealtimeDeploymentName, afterSave.RealtimeDeploymentName);
    }

    [Fact]
    public async Task ResolveConversationModeAsync_WhenAProfileNamesAConversationDeploymentButChoseTextInput_ShouldStayText()
    {
        // Arrange. A profile stores its own chat mode, so TextInput is a choice its author made. A conversation
        // deployment left behind in its settings must not resurrect voice -- the opposite of the interaction
        // rule above, and the reason the two are told apart rather than guessed.
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("gpt-5", AIDeploymentFeatureNames.TextGeneration),
            CreateDeployment("gpt-realtime", AIDeploymentFeatureNames.Realtime));

        // Act
        var resolution = await manager.ResolveConversationModeAsync(
            ChatMode.TextInput,
            conversationDeploymentName: "gpt-realtime",
            chatDeploymentName: "gpt-5",
            hasSpeechToText: false,
            hasTextToSpeech: false,
            chatModeIsSiteWide: false,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(resolution.RealtimeEnabled);
        Assert.Equal(ChatMode.TextInput, resolution.ChatMode);
    }

    [Fact]
    public async Task ResolveConversationModeAsync_WhenTheProfileIsMigratedAndSaved_ShouldResolveTheSameWayAsBeforeTheSave()
    {
        // Arrange. The fold is a read-time translation, so the editors write the folded shape back on the next
        // save. Both shapes must reach the same realtime deployment, or a profile would change behavior simply
        // by being opened and saved.
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("gpt-5", AIDeploymentFeatureNames.TextGeneration),
            CreateDeployment("gpt-realtime", AIDeploymentFeatureNames.Realtime));

        // Act
        var beforeSave = await manager.ResolveConversationModeAsync(
            ChatMode.TextInput,
            conversationDeploymentName: null,
            chatDeploymentName: "gpt-realtime",
            hasSpeechToText: false,
            hasTextToSpeech: false,
            cancellationToken: TestContext.Current.CancellationToken);

        var afterSave = await manager.ResolveConversationModeAsync(
            ChatMode.Conversation,
            conversationDeploymentName: "gpt-realtime",
            chatDeploymentName: null,
            hasSpeechToText: false,
            hasTextToSpeech: false,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(beforeSave.ChatMode, afterSave.ChatMode);
        Assert.Equal(beforeSave.RealtimeEnabled, afterSave.RealtimeEnabled);
        Assert.Equal(beforeSave.RealtimeDeploymentName, afterSave.RealtimeDeploymentName);
    }

    [Fact]
    public async Task ResolveConversationModeAsync_WhenTheProfileIsMigrated_ShouldLeaveTheTextModelToTheChatSlot()
    {
        // Arrange. The migrated profile still carries a realtime model in ChatDeploymentName. The chat slot
        // excludes realtime deployments, so a typed turn lands on the site's chat default instead -- which is
        // the same end state as blanking the field, without rewriting stored data.
        var settings = new DefaultAIDeploymentSettings
        {
            DefaultChatDeploymentName = "site-chat",
        };

        var manager = CreateManager(
            settings,
            CreateDeployment("site-chat", AIDeploymentFeatureNames.TextGeneration),
            CreateDeployment("gpt-realtime", AIDeploymentFeatureNames.Realtime));

        // Act
        var chatDeployment = await manager.ResolveSlotAsync(
            AIDeploymentSlotNames.Chat,
            "gpt-realtime",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("site-chat", chatDeployment.Name);
    }

    [Fact]
    public async Task ResolveConversationModeAsync_ShouldNotOfferRealtimeDeploymentsInTheChatPicker()
    {
        // Arrange. The chat deployment is a text model again, so the picker that fills it must stop listing
        // speech-to-speech models.
        var manager = CreateManager(
            new DefaultAIDeploymentSettings(),
            CreateDeployment("gpt-5", AIDeploymentFeatureNames.TextGeneration),
            CreateDeployment("gpt-realtime", AIDeploymentFeatureNames.Realtime));

        // Act
        var chatDeployments = await manager.GetAllBySlotAsync(AIDeploymentSlotNames.Chat, cancellationToken: TestContext.Current.CancellationToken);
        var conversationDeployments = await manager.GetAllBySlotAsync(AIDeploymentSlotNames.Realtime, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["gpt-5"], chatDeployments.Select(d => d.Name));
        Assert.Equal(["gpt-realtime"], conversationDeployments.Select(d => d.Name));
    }
}
