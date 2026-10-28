using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Speech;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

#pragma warning disable MEAI001 // This client type is for evaluation purposes only and may change in future updates.
/// <summary>
/// Applies a profile's <see cref="AIProfileSpeechSettings"/> to the speech clients and options used for its
/// conversations, the same way for the chat hub and the cascaded realtime pipeline.
/// </summary>
public static class ProfileSpeech
{
    /// <summary>
    /// The slowest and fastest speaking rate a profile may ask for.
    /// </summary>
    public const double MinimumSpeakingRate = 0.25;

    /// <inheritdoc cref="MinimumSpeakingRate"/>
    public const double MaximumSpeakingRate = 4;

    /// <summary>
    /// Reads the profile's speech settings, or returns <see langword="null"/> when it has none.
    /// </summary>
    /// <param name="resource">The profile (other resources have no speech settings).</param>
    public static AIProfileSpeechSettings GetSettings(object resource)
    {
        return resource is AIProfile profile && profile.TryGetSettings<AIProfileSpeechSettings>(out var settings)
            ? settings
            : null;
    }

    /// <summary>
    /// Clamps a speaking rate into the supported range. A missing, zero, negative or non-finite rate means "use the
    /// deployment's pace" and returns <see langword="null"/>.
    /// </summary>
    /// <param name="rate">The configured rate.</param>
    public static double? NormalizeSpeakingRate(double? rate)
    {
        if (rate is not > 0 || !double.IsFinite(rate.Value))
        {
            return null;
        }

        return Math.Clamp(rate.Value, MinimumSpeakingRate, MaximumSpeakingRate);
    }

    /// <summary>
    /// Wraps a text-to-speech client with the profile's fallback deployment and pronunciation dictionary. Returns the
    /// client unchanged when the profile configures neither.
    /// </summary>
    /// <param name="primary">The client for the profile's text-to-speech deployment.</param>
    /// <param name="settings">The profile's speech settings, if any.</param>
    /// <param name="deploymentManager">Used to find the fallback deployment.</param>
    /// <param name="clientFactory">Used to create the fallback client.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task<ITextToSpeechClient> DecorateAsync(
        ITextToSpeechClient primary,
        AIProfileSpeechSettings settings,
        IAIDeploymentManager deploymentManager,
        IAIClientFactory clientFactory,
        ILogger logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(primary);

        if (settings is null)
        {
            return primary;
        }

        var client = primary;

        if (!string.IsNullOrWhiteSpace(settings.FallbackTextToSpeechDeploymentName))
        {
            var fallback = await CreateFallbackAsync(settings.FallbackTextToSpeechDeploymentName, deploymentManager, clientFactory, logger, cancellationToken);

            if (fallback is not null)
            {
                client = new FallbackTextToSpeechClient(client, fallback, logger);
            }
        }

        var dictionary = PronunciationDictionary.Create(settings.PronunciationEntries);

        if (!dictionary.IsEmpty)
        {
            client = new PronunciationTextToSpeechClient(client, dictionary);
        }

        return NormalizeSpeakingRate(settings.SpeakingRate) is null ? client : new SpeakingRateTextToSpeechClient(client, settings);
    }

    /// <summary>
    /// Wraps a speech-to-text client so the profile's boosted keywords and recognition languages reach the provider.
    /// Returns the client unchanged when the profile configures neither.
    /// </summary>
    /// <param name="client">The client for the profile's speech-to-text deployment.</param>
    /// <param name="settings">The profile's speech settings, if any.</param>
    public static ISpeechToTextClient Decorate(ISpeechToTextClient client, AIProfileSpeechSettings settings)
    {
        ArgumentNullException.ThrowIfNull(client);

        return settings is not null && (Clean(settings.BoostedKeywords).Count > 0 || Clean(settings.RecognitionLanguages).Count > 0)
            ? new ProfileSpeechToTextClient(client, settings)
            : client;
    }

    /// <summary>
    /// Creates the fallback text-to-speech client, or returns <see langword="null"/> (and logs) when the named
    /// deployment cannot be used. A broken fallback must never stop the primary from speaking.
    /// </summary>
    public static async Task<ITextToSpeechClient> CreateFallbackAsync(
        string deploymentName,
        IAIDeploymentManager deploymentManager,
        IAIClientFactory clientFactory,
        ILogger logger = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var deployment = await deploymentManager.FindByNameAsync(deploymentName, cancellationToken);

            if (deployment is null)
            {
                logger?.LogWarning("The fallback text-to-speech deployment '{Deployment}' was not found; it will not be used.", deploymentName);

                return null;
            }

            return await clientFactory.CreateTextToSpeechClientAsync(deployment);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "The fallback text-to-speech deployment '{Deployment}' could not be created; it will not be used.", deploymentName);

            return null;
        }
    }

    /// <summary>
    /// Applies the profile's speaking rate to text-to-speech options. The rate multiplies any pace already set.
    /// </summary>
    /// <param name="options">The options to change.</param>
    /// <param name="settings">The profile's speech settings, if any.</param>
    public static void ApplySpeakingRate(TextToSpeechOptions options, AIProfileSpeechSettings settings)
    {
        ArgumentNullException.ThrowIfNull(options);

        var rate = NormalizeSpeakingRate(settings?.SpeakingRate);

        if (rate is not null)
        {
            options.Speed = (float)((options.Speed ?? 1f) * rate.Value);
        }
    }

    /// <summary>
    /// Adds the profile's boosted keywords and recognition languages to speech-to-text options for a provider that
    /// can use them. A language already set on the options is kept.
    /// </summary>
    /// <param name="options">The options to change.</param>
    /// <param name="settings">The profile's speech settings, if any.</param>
    public static void ApplyRecognition(SpeechToTextOptions options, AIProfileSpeechSettings settings)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (settings is null)
        {
            return;
        }

        var keywords = Clean(settings.BoostedKeywords);
        var languages = Clean(settings.RecognitionLanguages);

        if (keywords.Count > 0)
        {
            options.AdditionalProperties ??= [];
            options.AdditionalProperties[SpeechToTextOptionsProperties.BoostedKeywords] = keywords;
        }

        if (languages.Count > 0)
        {
            options.AdditionalProperties ??= [];
            options.AdditionalProperties[SpeechToTextOptionsProperties.RecognitionLanguages] = languages;
            options.SpeechLanguage = string.IsNullOrWhiteSpace(options.SpeechLanguage) ? languages[0] : options.SpeechLanguage;
        }
    }

    /// <summary>
    /// Gets the first non-blank value of a list, or <see langword="null"/>.
    /// </summary>
    /// <param name="values">The configured values.</param>
    public static string FirstOrDefault(IEnumerable<string> values)
    {
        var cleaned = Clean(values);

        return cleaned.Count > 0 ? cleaned[0] : null;
    }

    /// <summary>
    /// Trims a list of words, dropping blanks and duplicates while keeping order.
    /// </summary>
    /// <param name="values">The configured values.</param>
    public static IReadOnlyList<string> Clean(IEnumerable<string> values)
    {
        return values is null
            ? []
            : values.Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}
#pragma warning restore MEAI001
