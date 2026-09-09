using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Holds the deployment slots that the framework and modules contribute. A slot pairs a required model
/// capability with a site-wide default and an optional fallback slot, replacing the fixed
/// deployment-purpose switch that used to drive deployment resolution.
/// </summary>
public sealed class AIDeploymentSlotOptions
{
    /// <summary>
    /// Gets the registered slots keyed by their technical name.
    /// </summary>
    public IDictionary<string, AIDeploymentSlotDescriptor> Slots { get; } = new Dictionary<string, AIDeploymentSlotDescriptor>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds a slot, or updates the definition when the slot is already registered.
    /// </summary>
    /// <param name="name">The technical name of the slot.</param>
    /// <param name="displayName">The display text shown to operators.</param>
    /// <param name="configure">An optional delegate used to further configure the descriptor.</param>
    public AIDeploymentSlotOptions AddSlot(string name, LocalizedString displayName, Action<AIDeploymentSlotDescriptor> configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!Slots.TryGetValue(name, out var descriptor))
        {
            descriptor = new AIDeploymentSlotDescriptor();
            Slots[name] = descriptor;
        }

        descriptor.Name = name;

        if (displayName is not null)
        {
            descriptor.DisplayName = displayName;
        }

        descriptor.DisplayName ??= new LocalizedString(name, name);
        configure?.Invoke(descriptor);

        return this;
    }

    /// <summary>
    /// Finds a registered slot by its technical name.
    /// </summary>
    /// <param name="name">The technical name of the slot.</param>
    /// <returns>The descriptor, or <see langword="null"/> when the slot is not registered.</returns>
    public AIDeploymentSlotDescriptor Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return Slots.TryGetValue(name, out var descriptor)
            ? descriptor
            : null;
    }

    /// <summary>
    /// Creates the slot registry that ships with the framework.
    /// </summary>
    /// <remarks>
    /// Used to configure the DI options and as the fallback for a deployment manager that was constructed
    /// without a slot registry. A manager built that way does not see module-registered slots.
    /// </remarks>
    public static AIDeploymentSlotOptions CreateDefault()
    {
        var options = new AIDeploymentSlotOptions();

        options
            .AddSlot(AIDeploymentSlotNames.Chat, new LocalizedString(AIDeploymentSlotNames.Chat, "Chat"), slot =>
            {
                slot.RequiredFeature = AIDeploymentFeatureNames.TextGeneration;
                slot.ExcludedFeature = AIDeploymentFeatureNames.Realtime;
                slot.GetDefaultDeploymentName = static settings => settings.DefaultChatDeploymentName;
                slot.AllowUnconstrained = true;
                slot.Order = 10;
            })
            .AddSlot(AIDeploymentSlotNames.Utility, new LocalizedString(AIDeploymentSlotNames.Utility, "Utility"), slot =>
            {
                slot.RequiredFeature = AIDeploymentFeatureNames.TextGeneration;
                slot.ExcludedFeature = AIDeploymentFeatureNames.Realtime;
                slot.GetDefaultDeploymentName = static settings => settings.DefaultUtilityDeploymentName;

                // Background work belongs on the profile's own chat deployment when no utility deployment is
                // configured. Without this the utility slot's own "first capable" tail would answer first and
                // route summarization to an unrelated model.
                slot.FallbackSlotName = AIDeploymentSlotNames.Chat;
                slot.AllowUnconstrained = true;
                slot.Order = 20;
            })
            .AddSlot(AIDeploymentSlotNames.Embedding, new LocalizedString(AIDeploymentSlotNames.Embedding, "Embedding"), slot =>
            {
                slot.RequiredFeature = AIDeploymentFeatureNames.TextEmbedding;
                slot.GetDefaultDeploymentName = static settings => settings.DefaultEmbeddingDeploymentName;
                slot.Order = 30;
            })
            .AddSlot(AIDeploymentSlotNames.Image, new LocalizedString(AIDeploymentSlotNames.Image, "Image generation"), slot =>
            {
                slot.RequiredFeature = AIDeploymentFeatureNames.ImageOutput;
                slot.GetDefaultDeploymentName = static settings => settings.DefaultImageDeploymentName;
                slot.Order = 40;
            })
            .AddSlot(AIDeploymentSlotNames.Vision, new LocalizedString(AIDeploymentSlotNames.Vision, "Vision"), slot =>
            {
                slot.RequiredFeature = AIDeploymentFeatureNames.ImageInput;
                slot.GetDefaultDeploymentName = static settings => settings.DefaultVisionDeploymentName;
                slot.Order = 50;
            })
            .AddSlot(AIDeploymentSlotNames.SpeechToText, new LocalizedString(AIDeploymentSlotNames.SpeechToText, "Speech to text"), slot =>
            {
                slot.RequiredFeature = AIDeploymentFeatureNames.SpeechToText;
                slot.GetDefaultDeploymentName = static settings => settings.DefaultSpeechToTextDeploymentName;
                slot.Order = 60;
            })
            .AddSlot(AIDeploymentSlotNames.TextToSpeech, new LocalizedString(AIDeploymentSlotNames.TextToSpeech, "Text to speech"), slot =>
            {
                slot.RequiredFeature = AIDeploymentFeatureNames.TextToSpeech;
                slot.GetDefaultDeploymentName = static settings => settings.DefaultTextToSpeechDeploymentName;
                slot.Order = 70;
            })
            .AddSlot(AIDeploymentSlotNames.Realtime, new LocalizedString(AIDeploymentSlotNames.Realtime, "Realtime"), slot =>
            {
                // Realtime never fitted the purpose switch, so its resolution was re-implemented in the
                // orchestrator and both hubs. As a slot it resolves through the same chain as everything else.
                slot.RequiredFeature = AIDeploymentFeatureNames.Realtime;
                slot.GetDefaultDeploymentName = static settings => settings.DefaultRealtimeDeploymentName;
                slot.Order = 80;
            });

        return options;
    }
}
