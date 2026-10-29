using System.Net;
using System.Text;
using System.Text.Json;

namespace CrestApps.Core.AI.Cartesia.Services;

/// <summary>
/// The Cartesia HTTP API: builds authenticated, versioned requests and turns failures into readable exceptions.
/// </summary>
internal sealed class CartesiaApi
{
    private const int MaximumErrorLength = 500;

    private readonly HttpClient _http;
    private readonly Uri _baseAddress;
    private readonly string _apiKey;

    public CartesiaApi(HttpClient http, Uri endpoint, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        _http = http;
        _apiKey = apiKey;

        var address = (endpoint ?? new Uri(CartesiaConstants.DefaultEndpoint)).AbsoluteUri;
        _baseAddress = new Uri(address.EndsWith('/') ? address : address + "/");
    }

    public HttpRequestMessage CreateRequest(HttpMethod method, string relativePath, HttpContent content = null)
    {
        var request = new HttpRequestMessage(method, new Uri(_baseAddress, relativePath)) { Content = content };
        request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        request.Headers.TryAddWithoutValidation("Cartesia-Version", CartesiaConstants.ApiVersion);

        return request;
    }

    public static StringContent Json(object body)
        => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, bool streaming, CancellationToken cancellationToken)
    {
        var response = await _http.SendAsync(
            request,
            streaming ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var message = await DescribeFailureAsync(response, cancellationToken);
            response.Dispose();

            throw new HttpRequestException(message, inner: null, response.StatusCode);
        }

        return response;
    }

    public async Task<JsonDocument> GetJsonAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, streaming: false, cancellationToken);

        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }

    private static async Task<string> DescribeFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var detail = body.Length > MaximumErrorLength ? body[..MaximumErrorLength] : body;
        var hint = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => " Check the connection's API key.",
            HttpStatusCode.TooManyRequests => " Cartesia is rate limiting this key or the plan's concurrency limit was reached.",
            _ => string.Empty,
        };

        return $"Cartesia answered {(int)response.StatusCode} ({response.ReasonPhrase}).{hint} {detail}".TrimEnd();
    }
}
