#pragma warning disable OPENAI001 // The AuthenticationPolicy constructor of the OpenAI SDK is for evaluation purposes only.
#pragma warning disable OPENAI002 // The OpenAI Realtime API is experimental and requires explicit opt-in at each usage site.
using System.ClientModel.Primitives;
using OpenAI.Realtime;

namespace CrestApps.Core.AI.OpenAI.Azure.Services;

/// <summary>
/// The OpenAI SDK's <see cref="RealtimeClient"/> for an Azure OpenAI resource. The SDK puts an <c>Authorization</c>
/// header on the WebSocket handshake only for the OpenAI API key it was constructed with and leaves every other
/// credential to the caller, so this client adds the connection's header to each session itself.
/// </summary>
internal sealed class AzureRealtimeClient : RealtimeClient
{
    private readonly Func<CancellationToken, ValueTask<KeyValuePair<string, string>>> _authenticationHeader;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureRealtimeClient"/> class.
    /// </summary>
    /// <param name="authenticationPolicy">The authentication policy of the HTTP pipeline.</param>
    /// <param name="options">The client options, which carry the resource's <c>/openai/v1/</c> endpoint.</param>
    /// <param name="authenticationHeader">Provides the header that authenticates a WebSocket handshake.</param>
    public AzureRealtimeClient(
        AuthenticationPolicy authenticationPolicy,
        RealtimeClientOptions options,
        Func<CancellationToken, ValueTask<KeyValuePair<string, string>>> authenticationHeader)
        : base(authenticationPolicy, options)
    {
        _authenticationHeader = authenticationHeader;
    }

    /// <summary>
    /// Starts a realtime session with the connection's authentication header on the WebSocket handshake.
    /// </summary>
    /// <param name="model">The deployment name.</param>
    /// <param name="intent">The session intent, such as <c>transcription</c>, or <see langword="null"/>.</param>
    /// <param name="options">The session options, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task<RealtimeSessionClient> StartSessionAsync(string model, string intent, RealtimeSessionClientOptions options = null, CancellationToken cancellationToken = default)
    {
        options ??= new RealtimeSessionClientOptions();

        var header = await _authenticationHeader(cancellationToken);

        options.Headers[header.Key] = header.Value;

        return await base.StartSessionAsync(model, intent, options, cancellationToken);
    }
}
