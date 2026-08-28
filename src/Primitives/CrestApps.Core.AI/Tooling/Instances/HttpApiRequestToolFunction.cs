using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.AI.Tooling.Parameters;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Tooling.Instances;

/// <summary>
/// An <see cref="AIFunction"/> that issues an HTTP request to a user-configured endpoint. The endpoint,
/// HTTP method, authentication, and static headers are captured up front in
/// <see cref="HttpApiRequestToolSettings"/>; the AI model only supplies the open arguments (relative
/// path, query parameters, and request body) that the settings allow.
/// </summary>
public sealed class HttpApiRequestToolFunction : AIFunction
{
    private const int _maxResponseLength = 16000;
    private static readonly char[] _headerLineBreaks = ['\r', '\n'];
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
    private readonly AIToolInstance _instance;
    private readonly IReadOnlyList<AIToolInstanceParameter> _parameters;
    private readonly JsonElement _jsonSchema;
    private readonly Dictionary<string, object> _additionalProperties;
    private readonly bool _exposePath;
    private readonly bool _exposeQuery;
    private readonly bool _exposeBody;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpApiRequestToolFunction"/> class.
    /// </summary>
    /// <param name="name">The function name exposed to the AI model.</param>
    /// <param name="description">The description exposed to the AI model.</param>
    /// <param name="settings">The user-provided settings that configure the request.</param>
    /// <param name="instance">
    /// The configured tool instance the function belongs to. Used to cache OAuth 2.0 token state across
    /// requests. May be <see langword="null"/> (for example in tests), in which case token caching is
    /// performed in-memory for the lifetime of the function only.
    /// </param>
    public HttpApiRequestToolFunction(
        string name,
        string description,
        HttpApiRequestToolSettings settings,
        AIToolInstance instance = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(settings);

        _name = name;
        _description = string.IsNullOrWhiteSpace(description)
            ? name
            : description;
        _settings = settings;
        _instance = instance;
        _parameters = AIToolParameterBinder.GetParameters(instance);

        // A placement the user has bound a parameter to is no longer left open for the model to fill
        // freely. A typed, described parameter is strictly better than an untyped bag, and closing the
        // free-form argument is what makes strict schema mode reachable.
        _exposePath = settings.AllowModelProvidedPath && !HasBinding(HttpApiRequestParameterBindings.Path);
        _exposeQuery = settings.AllowModelProvidedQuery && !HasBinding(HttpApiRequestParameterBindings.Query);
        _exposeBody = settings.AllowModelProvidedBody && !HasBinding(HttpApiRequestParameterBindings.Body);

        _jsonSchema = AIToolParameterSchemaBuilder.Merge(
            BuildBaseSchema(_exposePath, _exposeQuery, _exposeBody),
            _parameters);

        _additionalProperties = new Dictionary<string, object>
        {
            ["Strict"] = AIToolParameterSchemaBuilder.IsStrictEligible(_exposePath || _exposeQuery || _exposeBody, _parameters),
        };
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
    /// Gets additional metadata applied to the function. Strict mode is enabled only when every argument
    /// is a required declared parameter; the open-ended path, query, and body arguments cannot be
    /// expressed under a strict schema.
    /// </summary>
    public override IReadOnlyDictionary<string, object> AdditionalProperties => _additionalProperties;

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

        var resolution = AIToolParameterBinder.Resolve(
            _parameters,
            arguments,
            services,
            value => Unprotect(services, value));

        if (!resolution.Succeeded)
        {
            // Reported back to the model rather than thrown, so it can correct the call and retry.
            if (logger?.IsEnabled(LogLevel.Debug) == true)
            {
                logger.LogDebug(
                    "AI tool '{ToolName}' rejected a call with unusable parameters: {Errors}",
                    _name, string.Join(" ", resolution.Errors));
            }

            return Error(string.Join(" ", resolution.Errors));
        }

        Uri requestUri;

        try
        {
            requestUri = BuildRequestUri(arguments, resolution);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "AI tool '{ToolName}' could not build the request URI.", _name);

            return Error($"The request URL could not be built: {ex.Message}");
        }

        var method = ResolveMethod();
        using var request = new HttpRequestMessage(method, requestUri);

        await ApplyAuthenticationAsync(request, services, logger, cancellationToken);
        ApplyDefaultHeaders(request, resolution, logger);
        ApplyBody(request, method, arguments, resolution);

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

    private Uri BuildRequestUri(AIFunctionArguments arguments, AIToolParameterResolution resolution)
    {
        var baseUri = new Uri(_settings.BaseUrl.Trim(), UriKind.Absolute);
        var url = baseUri.ToString();

        if (!string.IsNullOrWhiteSpace(_settings.PathTemplate))
        {
            url = CombineUrl(url, ApplyPathTemplate(_settings.PathTemplate.Trim(), resolution));
        }

        if (_exposePath &&
            TryGetString(arguments, "path", out var path) &&
            !string.IsNullOrWhiteSpace(path))
        {
            url = CombineUrl(url, path.Trim());
        }

        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);

        if (_exposeQuery &&
            arguments.TryGetValue("query", out var queryValue) &&
            TryReadObject(queryValue, out var query))
        {
            foreach (var pair in query)
            {
                if (pair.Value is not null)
                {
                    pairs[pair.Key] = pair.Value.ToString();
                }
            }
        }

        // Applied after the model's free-form query so a configured parameter always wins over a value
        // the model invented for the same key.
        foreach (var resolved in resolution.ForTarget(HttpApiRequestParameterBindings.Query))
        {
            var value = resolved.StringValue;

            if (value is not null && !string.IsNullOrEmpty(resolved.Binding.Name))
            {
                pairs[resolved.Binding.Name] = value;
            }
        }

        if (pairs.Count > 0)
        {
            url = QueryHelpers.AddQueryString(url, pairs);
        }

        var requestUri = new Uri(url, UriKind.Absolute);

        // The endpoint is fixed by the instance configuration; the model may only extend the path/query.
        // Reject any request that would leave the configured scheme/host/port to prevent SSRF redirection
        // to arbitrary (for example internal) hosts.
        if (requestUri.Scheme != baseUri.Scheme ||
            !string.Equals(requestUri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) ||
            requestUri.Port != baseUri.Port)
        {
            throw new InvalidOperationException("The resolved request URL must stay on the configured base host.");
        }

        return requestUri;
    }

    private HttpMethod ResolveMethod()
    {
        return string.IsNullOrWhiteSpace(_settings.HttpMethod)
            ? HttpMethod.Get
            : HttpMethod.Parse(_settings.HttpMethod.Trim().ToUpperInvariant());
    }

    private async Task ApplyAuthenticationAsync(
        HttpRequestMessage request,
        IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken)
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
                if (!string.IsNullOrWhiteSpace(_settings.Username))
                {
                    var password = Unprotect(services, _settings.Password);
                    var raw = $"{_settings.Username}:{password}";
                    var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));

                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
                }

                break;

            case HttpApiRequestAuthenticationType.OAuth2:
                var accessToken = await EnsureAccessTokenAsync(services, logger, cancellationToken);

                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                }

                break;
        }
    }

    private async Task<string> EnsureAccessTokenAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.TokenEndpoint))
        {
            logger?.LogWarning("AI tool '{ToolName}' is configured for OAuth2 but has no token endpoint.", _name);

            return null;
        }

        var timeProvider = services?.GetService<TimeProvider>() ?? TimeProvider.System;
        var now = timeProvider.GetUtcNow();

        HttpApiRequestTokenState state = null;

        if (_instance is not null && _instance.TryGet<HttpApiRequestTokenState>(out var cached))
        {
            state = cached;
        }

        if (state is not null &&
            !string.IsNullOrEmpty(state.AccessToken) &&
            state.ExpiresAtUtc is { } expiresAt &&
            expiresAt > now.AddSeconds(30))
        {
            return Unprotect(services, state.AccessToken);
        }

        var refreshToken = state is not null && !string.IsNullOrEmpty(state.RefreshToken)
            ? Unprotect(services, state.RefreshToken)
            : null;

        var result = await RequestTokenAsync(services, refreshToken, logger, cancellationToken);

        if (result is null || string.IsNullOrEmpty(result.AccessToken))
        {
            // Fall back to any previously cached token, even if it may be expired.
            return state is not null
                ? Unprotect(services, state.AccessToken)
                : null;
        }

        var expiresAtUtc = result.ExpiresInSeconds is > 0
            ? now.AddSeconds(result.ExpiresInSeconds.Value)
            : now.AddMinutes(55);

        var newState = new HttpApiRequestTokenState
        {
            AccessToken = Protect(services, result.AccessToken),
            RefreshToken = string.IsNullOrEmpty(result.RefreshToken)
                ? state?.RefreshToken
                : Protect(services, result.RefreshToken),
            TokenType = result.TokenType,
            ExpiresAtUtc = expiresAtUtc,
        };

        await PersistTokenStateAsync(services, newState, logger, cancellationToken);

        return result.AccessToken;
    }

    private async Task<OAuthTokenResult> RequestTokenAsync(
        IServiceProvider services,
        string refreshToken,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(refreshToken))
        {
            var refreshed = await PostTokenRequestAsync(services, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            }, logger, cancellationToken);

            if (refreshed is not null)
            {
                return refreshed;
            }
        }

        // When a resource owner username is configured, use the OAuth 2.0 password grant; otherwise fall
        // back to the client credentials grant.
        if (!string.IsNullOrWhiteSpace(_settings.Username))
        {
            return await PostTokenRequestAsync(services, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "password",
                ["username"] = _settings.Username.Trim(),
                ["password"] = Unprotect(services, _settings.Password) ?? string.Empty,
            }, logger, cancellationToken);
        }

        return await PostTokenRequestAsync(services, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
        }, logger, cancellationToken);
    }

    private async Task<OAuthTokenResult> PostTokenRequestAsync(
        IServiceProvider services,
        Dictionary<string, string> parameters,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var clientId = _settings.ClientId;
        var clientSecret = Unprotect(services, _settings.ClientSecret);

        if (!string.IsNullOrEmpty(clientId))
        {
            parameters["client_id"] = clientId;
        }

        if (!string.IsNullOrEmpty(clientSecret))
        {
            parameters["client_secret"] = clientSecret;
        }

        if (!string.IsNullOrWhiteSpace(_settings.Scope))
        {
            parameters["scope"] = _settings.Scope.Trim();
        }

        try
        {
            var httpClient = CreateHttpClient(services);
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, _settings.TokenEndpoint.Trim())
            {
                Content = new FormUrlEncodedContent(parameters),
            };

            using var response = await httpClient.SendAsync(tokenRequest, cancellationToken);
            var payload = response.Content is null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger?.LogWarning(
                    "AI tool '{ToolName}' token request to {Endpoint} failed with status {StatusCode}.",
                    _name, _settings.TokenEndpoint, (int)response.StatusCode);

                return null;
            }

            return ParseTokenResponse(payload);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "AI tool '{ToolName}' token request to {Endpoint} failed.", _name, _settings.TokenEndpoint);

            return null;
        }
    }

    private static OAuthTokenResult ParseTokenResponse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("access_token", out var accessTokenElement) ||
                accessTokenElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var result = new OAuthTokenResult
            {
                AccessToken = accessTokenElement.GetString(),
            };

            if (root.TryGetProperty("refresh_token", out var refreshElement) && refreshElement.ValueKind == JsonValueKind.String)
            {
                result.RefreshToken = refreshElement.GetString();
            }

            if (root.TryGetProperty("token_type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
            {
                result.TokenType = typeElement.GetString();
            }

            if (root.TryGetProperty("expires_in", out var expiresElement))
            {
                if (expiresElement.ValueKind == JsonValueKind.Number && expiresElement.TryGetInt32(out var seconds))
                {
                    result.ExpiresInSeconds = seconds;
                }
                else if (expiresElement.ValueKind == JsonValueKind.String &&
                    int.TryParse(expiresElement.GetString(), out var parsedSeconds))
                {
                    result.ExpiresInSeconds = parsedSeconds;
                }
            }

            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task PersistTokenStateAsync(
        IServiceProvider services,
        HttpApiRequestTokenState state,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (_instance is null)
        {
            return;
        }

        // Cache in-memory so a subsequent tool call within the same request reuses the token.
        _instance.Put(state);

        var catalog = services?.GetService<ICatalog<AIToolInstance>>();

        if (catalog is null)
        {
            return;
        }

        try
        {
            await catalog.UpdateAsync(_instance, cancellationToken);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "AI tool '{ToolName}' could not persist the cached OAuth2 token state.", _name);
        }
    }

    private void ApplyDefaultHeaders(HttpRequestMessage request, AIToolParameterResolution resolution, ILogger logger)
    {
        if (_settings.DefaultHeaders is not null)
        {
            foreach (var header in _settings.DefaultHeaders)
            {
                if (!string.IsNullOrWhiteSpace(header.Key))
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        foreach (var resolved in resolution.ForTarget(HttpApiRequestParameterBindings.Header))
        {
            var value = resolved.StringValue;

            if (string.IsNullOrEmpty(resolved.Binding.Name) || value is null)
            {
                continue;
            }

            // A line break in a header value splits the request. The source never accepts model-supplied
            // header values, but a context or fixed value is still checked before it reaches the wire.
            if (value.AsSpan().IndexOfAny(_headerLineBreaks) >= 0)
            {
                logger?.LogWarning(
                    "AI tool '{ToolName}' skipped header '{HeaderName}' because its value contained a line break.",
                    _name, resolved.Binding.Name);

                continue;
            }

            request.Headers.TryAddWithoutValidation(resolved.Binding.Name, value);
        }
    }

    private void ApplyBody(
        HttpRequestMessage request,
        HttpMethod method,
        AIFunctionArguments arguments,
        AIToolParameterResolution resolution)
    {
        if (!_bodyMethods.Contains(method.Method))
        {
            return;
        }

        object bodyValue = null;
        var hasModelBody = _exposeBody &&
            arguments.TryGetValue("body", out bodyValue) &&
            bodyValue is not null;

        var bound = new List<AIToolResolvedParameter>();

        foreach (var resolved in resolution.ForTarget(HttpApiRequestParameterBindings.Body))
        {
            bound.Add(resolved);
        }

        if (bound.Count == 0)
        {
            if (!hasModelBody)
            {
                return;
            }

            // No bound parameters, so the model's body is forwarded exactly as it was before parameter
            // support existed.
            var json = bodyValue switch
            {
                JsonElement element => element.GetRawText(),
                JsonNode node => node.ToJsonString(),
                string text => text,
                _ => JsonSerializer.Serialize(bodyValue),
            };

            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            return;
        }

        var target = hasModelBody
            ? ToJsonNode(bodyValue) as JsonObject ?? []
            : [];

        // Bound parameters are written last so a configured parameter wins over the same field supplied
        // by the model.
        foreach (var resolved in bound)
        {
            WriteBodyValue(target, resolved);
        }

        request.Content = new StringContent(target.ToJsonString(), Encoding.UTF8, "application/json");
    }

    private static string ApplyPathTemplate(string template, AIToolParameterResolution resolution)
    {
        var result = template;

        foreach (var resolved in resolution.ForTarget(HttpApiRequestParameterBindings.Path))
        {
            if (string.IsNullOrEmpty(resolved.Binding.Name))
            {
                continue;
            }

            // Escaping the value as a single data segment stops it adding path segments or traversing
            // upwards; the host check in BuildRequestUri is the second line of defense.
            result = result.Replace(
                "{" + resolved.Binding.Name + "}",
                Uri.EscapeDataString(resolved.StringValue ?? string.Empty),
                StringComparison.OrdinalIgnoreCase);
        }

        if (result.Contains('{', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The path template contains a token with no matching parameter value.");
        }

        return result;
    }

    private static void WriteBodyValue(JsonObject target, AIToolResolvedParameter resolved)
    {
        var segments = resolved.Binding.Name?.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments is not { Length: > 0 })
        {
            return;
        }

        var current = target;

        // A dotted binding such as customer.id creates the intermediate objects it needs.
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current[segments[i]] is JsonObject existing)
            {
                current = existing;

                continue;
            }

            var child = new JsonObject();
            current[segments[i]] = child;
            current = child;
        }

        current[segments[^1]] = ToJsonNode(resolved.Value);
    }

    private static JsonNode ToJsonNode(object value)
    {
        return value switch
        {
            null => null,
            JsonElement element => JsonNode.Parse(element.GetRawText()),
            JsonNode node => node.DeepClone(),
            string text => JsonValue.Create(text),
            _ => JsonSerializer.SerializeToNode(value),
        };
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

    private static string Protect(IServiceProvider services, string value)
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

        return provider
            .CreateProtector(HttpApiRequestToolConstants.DataProtectionPurpose)
            .Protect(value);
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
        // The model-provided path is always treated as a relative segment appended to the configured base
        // URL. Absolute URLs are never honored here so the model cannot redirect the request to a different
        // host; BuildRequestUri additionally verifies the final host matches the configured base host.
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

    private static JsonObject BuildBaseSchema(bool exposePath, bool exposeQuery, bool exposeBody)
    {
        var properties = new JsonObject();

        if (exposePath)
        {
            properties["path"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional relative path appended to the configured base URL.",
            };
        }

        if (exposeQuery)
        {
            properties["query"] = new JsonObject
            {
                ["type"] = "object",
                ["description"] = "Optional query string parameters to append to the request.",
                ["additionalProperties"] = new JsonObject { ["type"] = "string" },
            };
        }

        if (exposeBody)
        {
            properties["body"] = new JsonObject
            {
                ["type"] = "object",
                ["description"] = "Optional JSON request body to send for POST/PUT/PATCH/DELETE requests.",
                ["additionalProperties"] = true,
            };
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
        };
    }

    private bool HasBinding(string target)
    {
        foreach (var parameter in _parameters)
        {
            if (parameter is not null &&
                AIToolParameterBinding.TryParse(parameter.Binding, parameter.Name, out var binding) &&
                binding.Is(target))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class OAuthTokenResult
    {
        public string AccessToken { get; set; }

        public string RefreshToken { get; set; }

        public string TokenType { get; set; }

        public int? ExpiresInSeconds { get; set; }
    }
}
