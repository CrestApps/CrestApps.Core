using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Deployments;

/// <summary>
/// Resolves how a chat surface should carry its conversation: a realtime (speech-to-speech) session, the
/// client-driven speech-to-text plus text-to-speech cascade, microphone dictation, or plain typing.
/// </summary>
/// <remarks>
/// <para>
/// This is the one place that answers the question. Four chat surfaces and two hubs used to answer it
/// separately, each by asking whether the chat deployment declared the realtime capability and squashing the
/// chat mode when it did — which is why a realtime deployment turned the whole UI voice-only. Now the chat
/// mode decides, the chat deployment means only "the text model this profile talks to", and both kinds of
/// turn can share a thread.
/// </para>
/// </remarks>
public static class ConversationModeResolutionExtensions
{
    /// <summary>
    /// Resolves the conversation mode for a chat surface.
    /// </summary>
    /// <param name="deploymentManager">The deployment manager.</param>
    /// <param name="configuredChatMode">The chat mode stored on the profile, or configured site-wide for interactions.</param>
    /// <param name="conversationDeploymentName">The conversation deployment the resource named, if any.</param>
    /// <param name="chatDeploymentName">
    /// The resource's chat deployment, used only to fold a resource stored before the conversation deployment
    /// existed. See the remarks.
    /// </param>
    /// <param name="hasSpeechToText">Whether this installation has a speech-to-text deployment configured.</param>
    /// <param name="hasTextToSpeech">Whether this installation has a text-to-speech deployment configured.</param>
    /// <param name="chatModeIsSiteWide">
    /// Whether <paramref name="configuredChatMode"/> belongs to the site rather than to the resource, as it does
    /// for chat interactions. See the remarks.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <remarks>
    /// <para>
    /// <b>Why the fold happens here and not while deserializing.</b> A resource stored before this change names
    /// its speech-to-speech model as its chat deployment, and nothing else in the stored JSON says so — the
    /// previous migration already erased the <c>RealtimeDeploymentName</c> marker, and
    /// <see cref="ChatModeJsonConverter"/> reads the old <c>"Realtime"</c> chat mode back as
    /// <see cref="ChatMode.TextInput"/>. The only thing that distinguishes such a resource is the deployment's
    /// own capability, which needs the deployment catalog and so cannot be read from
    /// <c>IJsonOnDeserialized</c>. Folding at read time also keeps the stored shape honest: nothing is rewritten
    /// behind the user's back, and an editor that saves the resource persists the folded shape as a side effect
    /// of showing it.
    /// </para>
    /// <para>
    /// The fold sets the mode as well as the deployment. A resource in this state has
    /// <see cref="ChatMode.TextInput"/> stored — the editor hid the chat mode field entirely once the deployment
    /// was realtime — so moving only the deployment name would leave it resolving to text-only, which is the
    /// one thing the fold exists to prevent.
    /// </para>
    /// <para>
    /// A named deployment that is not realtime-capable is reported rather than resolved.
    /// <see cref="IAIDeploymentManager.ResolveSlotAsync"/> falls through to the site default when the name it is
    /// given does not qualify for the slot, so without this check a typo would silently move the conversation to
    /// a different model.
    /// </para>
    /// <para>
    /// <b>Where the chat mode lives changes what naming a deployment means.</b> A profile stores its own chat
    /// mode, so a profile set to <see cref="ChatMode.TextInput"/> chose that, and a conversation deployment left
    /// behind in its settings must not resurrect voice. A chat interaction has no chat mode of its own — the
    /// site holds it — so naming a conversation deployment on the interaction is the only way it can ask to
    /// speak, exactly as choosing a speech-to-speech model used to be. <paramref name="chatModeIsSiteWide"/> is
    /// which of the two this is.
    /// </para>
    /// </remarks>
    public static async ValueTask<ConversationModeResolution> ResolveConversationModeAsync(
        this IAIDeploymentManager deploymentManager,
        ChatMode configuredChatMode,
        string conversationDeploymentName,
        string chatDeploymentName,
        bool hasSpeechToText,
        bool hasTextToSpeech,
        bool chatModeIsSiteWide = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deploymentManager);

        var resolution = new ConversationModeResolution
        {
            ChatMode = configuredChatMode,
            RequestedDeploymentName = string.IsNullOrWhiteSpace(conversationDeploymentName)
                ? null
                : conversationDeploymentName.Trim(),
        };

        var effectiveMode = configuredChatMode;

        if (resolution.RequestedDeploymentName is null && !string.IsNullOrWhiteSpace(chatDeploymentName))
        {
            var foldedDeployment = await deploymentManager.ResolveSlotAsync(
                AIDeploymentSlotNames.Realtime,
                chatDeploymentName,
                cancellationToken: cancellationToken);

            if (NameMatches(foldedDeployment, chatDeploymentName))
            {
                resolution.RequestedDeploymentName = chatDeploymentName.Trim();
                resolution.FoldedFromChatDeployment = true;
                effectiveMode = ChatMode.Conversation;
            }
        }

        // A resource whose chat mode is not its own to set opts in by naming a conversation deployment. Without
        // this a chat interaction could never hold a voice conversation while the site-wide mode is text, which
        // is what it could always do before: choosing a speech-to-speech model on the interaction was the whole
        // opt-in. It would also lose that choice the first time its settings were saved, because the fold that
        // recovered it only fires while the conversation deployment is still empty.
        if (chatModeIsSiteWide && effectiveMode != ChatMode.Conversation && resolution.RequestedDeploymentName is not null)
        {
            effectiveMode = ChatMode.Conversation;
        }

        if (effectiveMode == ChatMode.Conversation)
        {
            var realtimeDeployment = await deploymentManager.ResolveSlotAsync(
                AIDeploymentSlotNames.Realtime,
                resolution.RequestedDeploymentName,
                cancellationToken: cancellationToken);

            if (resolution.RequestedDeploymentName is not null && !NameMatches(realtimeDeployment, resolution.RequestedDeploymentName))
            {
                // The named deployment exists but cannot run a realtime session, or does not exist at all. Either
                // way the slot answered with something the user did not ask for, so say nothing resolved.
                resolution.IsMisconfigured = true;
                realtimeDeployment = null;
            }

            if (realtimeDeployment is not null)
            {
                resolution.RealtimeEnabled = true;
                resolution.RealtimeDeploymentName = realtimeDeployment.Name;
            }
        }

        resolution.ChatMode = ResolveChatMode(effectiveMode, resolution.RealtimeEnabled, hasSpeechToText, hasTextToSpeech);

        return resolution;
    }

    /// <summary>
    /// Narrows a configured chat mode to what this installation's deployments can actually serve.
    /// </summary>
    /// <remarks>
    /// Conversation survives either because a realtime session carries it or because the speech-to-text and
    /// text-to-speech cascade can. Failing both it degrades to dictation, and failing that to typing.
    /// </remarks>
    private static ChatMode ResolveChatMode(ChatMode configuredChatMode, bool realtimeEnabled, bool hasSpeechToText, bool hasTextToSpeech)
    {
        switch (configuredChatMode)
        {
            case ChatMode.Conversation when realtimeEnabled || (hasSpeechToText && hasTextToSpeech):
                return ChatMode.Conversation;

            case ChatMode.Conversation when hasSpeechToText:
            case ChatMode.AudioInput when hasSpeechToText:
                return ChatMode.AudioInput;

            default:
                return ChatMode.TextInput;
        }
    }

    private static bool NameMatches(AIDeployment deployment, string name)
    {
        return deployment is not null && string.Equals(deployment.Name, name, StringComparison.OrdinalIgnoreCase);
    }
}
