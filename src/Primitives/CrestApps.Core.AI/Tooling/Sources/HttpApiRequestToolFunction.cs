using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Tooling.Sources;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Tooling.Sources;

/// <summary>
/// An <see cref="AIFunction"/> that issues an HTTP request to a user-configured endpoint. The endpoint,
/// HTTP method, authentication, and static headers are captured up front in
/// <see cref="HttpApiRequestToolSettings"/>; the AI model only supplies the open arguments (relative
/// path, query parameters, and request body) that the settings allow.
/// </summary>
public sealed class HttpApiRequestToolFunction : AIFunction
{
    private const int _maxResponseLength = 16000;
    private static readonly HashSet<string> _bodyMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST",
        "PUT",
        "PATCH",
        "DELETE",
    };

    private readonly string _name;
    private readonly string _description;
    private readonly HttpApiRequestToolSettings _settings;
    private readonly JsonElement _jsonSchema;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpApiRequestToolFunction"/> class.
    /// </summary>
    /// <param name="name">The function name exposed to the AI model.</param>
    /// <param name="description">The description exposed to the AI model.</param>
    /// <param name="settings">The user-provided settings that configure the request.</param>
    public HttpApiRequestToolFunction(string name, string description, HttpApiRequestToolSettings settings)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(settings);

        _name = name;
        _description = string.IsNullOrWhiteSpace(description)
            ? name
            : description;
        _settings = settings;
        _jsonSchema = BuildSchema(settings);
    }

    /// <summary>
    /// Gets the function name exposed to the AI model.
    /// </summary>
    public override string Name => _name;

    /// <summary>
    /// Gets the description exposed to the AI model.
    /// </summary>
    public override string Description => _description;

    /// <summary>
    /// Gets the JSON schema describing the open arguments the model may supply.
    /// </summary>
    public override JsonElement JsonSchema => _jsonSchema;

    /// <summary>
    /// Gets additional metadata applied to the function. Strict mode is disabled because the request
    /// body is an open-ended object.
    /// </summary>
    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } = new Dictionary<string, object>
    {
        ["Strict"] = false,
    };

    /// <summary>
    /// Issues the configured HTTP request, merging the model-provided arguments with the stored settings.
    /// </summary>
    /// <param name="arguments">The arguments supplied by the AI model.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var services = arguments.Services;
        var logger = services?.GetService<ILoggerFactory>()?.CreateLogger<HttpApiRequestToolFunction>();

        if (string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            return Error("This tool instance is missing its base URL configuration.");
        }

        Uri requestUri;

        try
        {
            requestUri = BuildRequestUri(arguments);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "AI tool '{ToolName}' could not build the request URI.", _name);

            return Error($"The request URL could not be built: {ex.Message}");
        }

        var method = ResolveMethod();
        using var request = new HttpRequestMessage(method, requestUri);

        ApplyAuthentication(request, services);
        ApplyDefaultHeaders(request);
        ApplyBody(request, method, arguments);

        var httpClient = CreateHttpClient(services);

        using var timeoutCts = CreateTimeoutScope(cancellationToken, out var effectiveToken);

        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, effectiveToken);
            var body = response.Content is null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(effectiveToken);
            var truncated = body.Length > _maxResponseLength;

            if (truncated)
            {
                body = body[.._maxResponseLength];
            }

            if (logger?.IsEnabled(LogLevel.Debug) == true)
            {
                logger.LogDebug("AI tool '{ToolName}' called {Method} {Uri} -> {StatusCode}.", _name, method.Method, requestUri, (int)response.StatusCode);
            }

            return JsonSerializer.Serialize(new
            {
                success = response.IsSuccessStatusCode,
                statusCode = (int)response.StatusCode,
                reasonPhrase = response.ReasonPhrase,
                contentType = response.Content?.Headers?.ContentType?.ToString(),
                truncated,
                body,
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "AI tool '{ToolName}' failed calling {Uri}.", _name, requestUri);

            return Error($"The request failed: {ex.Message}");
        }
    }

    private Uri BuildRequestUri(AIFunctionArguments arguments)
    {
        var url = _settings.BaseUrl.Trim();

        if (_settings.AllowModelProvidedPath &&
            TryGetString(arguments, "path", out var path) &&
            !string.IsNullOrWhiteSpace(path))
        {
            url = CombineUrl(url, path.Trim());
        }

        if (_settings.AllowModelProvidedQuery &&
            arguments.TryGetValue("query", out var queryValue) &&
            TryReadObject(queryValue, out var query))
        {
            var pairs = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var pair in query)
            {
                if (pair.Value is not null)
                {
                    pairs[pair.Key] = pair.Value.ToString();
                }
            }

            if (pairs.Count > 0)
            {
                url = QueryHelpers.AddQueryString(url, pairs);
            }
        }

        return new Uri(url, UriKind.Absolute);
    }

    private HttpMethod ResolveMethod()
    {
        return string.IsNullOrWhiteSpace(_settings.HttpMethod)
            ? HttpMethod.Get
            : HttpMethod.Parse(_settings.HttpMethod.Trim().ToUpperInvariant());
    }

    private void ApplyAuthentication(HttpRequestMessage request, IServiceProvider services)
    {
        switch (_settings.AuthenticationType)
        {
            case HttpApiRequestAuthenticationType.ApiKey:
                var apiKey = Unprotect(services, _settings.ApiKey);

                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    var headerName = string.IsNullOrWhiteSpace(_settings.ApiKeyHeaderName)
                        ? "X-Api-Key"
                        : _settings.ApiKeyHeaderName.Trim();

                    request.Headers.TryAddWithoutValidation(headerName, apiKey);
                }

                break;

            case HttpApiRequestAuthenticationType.Bearer:
                var token = Unprotect(services, _settings.BearerToken);

                if (!string.IsNullOrWhiteSpace(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                break;

            case HttpApiRequestAuthenticationType.Basic:
                if (!string.IsNullOrWhiteSpace(_settings.BasicUsername))
                {
                    var password = Unprotect(services, _settings.BasicPassword);
                    var raw = $"{_settings.BasicUsername}:{password}";
                    var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));

                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
                }

                break;
        }
    }

    private void ApplyDefaultHeaders(HttpRequestMessage request)
    {
        if (_settings.DefaultHeaders is null)
        {
            return;
        }

        foreach (var header in _settings.DefaultHeaders)
        {
            if (!string.IsNullOrWhiteSpace(header.Key))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
    }

    private void ApplyBody(HttpRequestMessage request, HttpMethod method, AIFunctionArguments arguments)
    {
        if (!_settings.AllowModelProvidedBody ||
            !_bodyMethods.Contains(method.Method) ||
            !arguments.TryGetValue("body", out var bodyValue) ||
            bodyValue is null)
        {
            return;
        }

        var json = bodyValue switch
        {
            JsonElement element => element.GetRawText(),
            JsonNode node => node.ToJsonString(),
            string text => text,
            _ => JsonSerializer.Serialize(bodyValue),
        };

        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static HttpClient CreateHttpClient(IServiceProvider services)
    {
        var factory = services?.GetService<IHttpClientFactory>();

        return factory is not null
            ? factory.CreateClient(HttpApiRequestToolConstants.HttpClientName)
            : new HttpClient();
    }

    private IDisposable CreateTimeoutScope(CancellationToken cancellationToken, out CancellationToken effectiveToken)
    {
        if (_settings.TimeoutSeconds is > 0)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds.Value));
            effectiveToken = cts.Token;

            return cts;
        }

        effectiveToken = cancellationToken;

        return new NoopDisposable();
    }

    private static string Unprotect(IServiceProvider services, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var provider = services?.GetService<IDataProtectionProvider>();

        if (provider is null)
        {
            return value;
        }

        try
        {
            return provider
                .CreateProtector(HttpApiRequestToolConstants.DataProtectionPurpose)
                .Unprotect(value);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // The value was not protected (for example, seeded from configuration); use it as-is.
            return value;
        }
    }

    private static string CombineUrl(string baseUrl, string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    private static bool TryGetString(AIFunctionArguments arguments, string key, out string value)
    {
        if (arguments.TryGetValue(key, out var raw) && raw is not null)
        {
            value = raw is JsonElement { ValueKind: JsonValueKind.String } element
                ? element.GetString()
                : raw.ToString();

            return !string.IsNullOrEmpty(value);
        }

        value = null;

        return false;
    }

    private static bool TryReadObject(object value, out IReadOnlyDictionary<string, object> result)
    {
        switch (value)
        {
            case JsonElement { ValueKind: JsonValueKind.Object } element:
                var fromElement = new Dictionary<string, object>(StringComparer.Ordinal);

                foreach (var property in element.EnumerateObject())
                {
                    fromElement[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.GetRawText();
                }

                result = fromElement;

                return true;

            case IReadOnlyDictionary<string, object> dictionary:
                result = dictionary;

                return true;

            default:
                result = null;

                return false;
        }
    }

    private static string Error(string message)
        => JsonSerializer.Serialize(new { success = false, error = message });

    private static JsonElement BuildSchema(HttpApiRequestToolSettings settings)
    {
        var properties = new JsonObject();

        if (settings.AllowModelProvidedPath)
        {
            properties["path"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional relative path appended to the configured base URL.",
            };
        }

        if (settings.AllowModelProvidedQuery)
        {
            properties["query"] = new JsonObject
            {
                ["type"] = "object",
                ["description"] = "Optional query string parameters to append to the request.",
                ["additionalProperties"] = new JsonObject { ["type"] = "string" },
            };
        }

        if (settings.AllowModelProvidedBody)
        {
            properties["body"] = new JsonObject
            {
                ["type"] = "object",
                ["description"] = "Optional JSON request body to send for POST/PUT/PATCH/DELETE requests.",
                ["additionalProperties"] = true,
            };
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };

        return JsonSerializer.SerializeToElement(schema);
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
