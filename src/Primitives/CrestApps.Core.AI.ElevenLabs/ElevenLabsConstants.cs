namespace CrestApps.Core.AI.ElevenLabs;

/// <summary>
/// Names used by the ElevenLabs provider.
/// </summary>
public static class ElevenLabsConstants
{
    /// <summary>
    /// The provider (client) name deployments and connections use.
    /// </summary>
    public const string ClientName = "ElevenLabs";

    /// <summary>
    /// The base address of the ElevenLabs API. A connection's <c>Endpoint</c> overrides it, for a data-residency
    /// region such as <c>https://api.eu.residency.elevenlabs.io</c>.
    /// </summary>
    public const string DefaultEndpoint = "https://api.elevenlabs.io";

    /// <summary>
    /// The text-to-speech model used when a deployment names none: low latency, 32 languages.
    /// </summary>
    public const string DefaultTextToSpeechModel = "eleven_flash_v2_5";

    /// <summary>
    /// The speech-to-text model used when a deployment names none.
    /// </summary>
    public const string DefaultSpeechToTextModel = "scribe_v1";

    /// <summary>
    /// The name of the <see cref="System.Net.Http.IHttpClientFactory"/> client the provider uses.
    /// </summary>
    public const string HttpClientName = "CrestApps.ElevenLabs";
}
