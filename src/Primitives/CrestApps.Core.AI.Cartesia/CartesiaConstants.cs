namespace CrestApps.Core.AI.Cartesia;

/// <summary>
/// Names used by the Cartesia provider.
/// </summary>
public static class CartesiaConstants
{
    /// <summary>
    /// The provider (client) name deployments and connections use.
    /// </summary>
    public const string ClientName = "Cartesia";

    /// <summary>
    /// The base address of the Cartesia API. A connection's <c>Endpoint</c> overrides it.
    /// </summary>
    public const string DefaultEndpoint = "https://api.cartesia.ai";

    /// <summary>
    /// The API version sent in the <c>Cartesia-Version</c> header, which pins the request and response shapes.
    /// </summary>
    public const string ApiVersion = "2025-04-16";

    /// <summary>
    /// The text-to-speech model used when a deployment names none.
    /// </summary>
    public const string DefaultTextToSpeechModel = "sonic-2";

    /// <summary>
    /// The speech-to-text model used when a deployment names none.
    /// </summary>
    public const string DefaultSpeechToTextModel = "ink-whisper";

    /// <summary>
    /// The API version the realtime speech-to-text WebSocket is pinned to, sent as its <c>cartesia_version</c> query
    /// parameter.
    /// </summary>
    public const string RealtimeApiVersion = "2026-08-14";

    /// <summary>
    /// The speech-to-text model a realtime transcription uses when the deployment names none, or names one the realtime
    /// endpoint does not offer, such as <see cref="DefaultSpeechToTextModel"/>.
    /// </summary>
    public const string DefaultRealtimeSpeechToTextModel = "ink-2";

    /// <summary>
    /// The name of the <see cref="System.Net.Http.IHttpClientFactory"/> client the provider uses.
    /// </summary>
    public const string HttpClientName = "CrestApps.Cartesia";
}
