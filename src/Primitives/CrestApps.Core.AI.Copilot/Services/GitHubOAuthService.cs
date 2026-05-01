using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using CrestApps.Core.AI.Copilot.Models;
using GitHub.Copilot.SDK;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Copilot.Services;

/// <summary>
/// Implementation of GitHub OAuth service for Copilot.
/// </summary>
public sealed class GitHubOAuthService
{
    private const string ProtectorPurpose = "CrestApps.Core.AI.Copilot.GitHubTokens";
    private const string StateProtectorPurpose = "CrestApps.Core.AI.Copilot.GitHubOAuthState";
    private const string StateCookieName = ".crestapps.gh-oauth-state";

    private readonly ICopilotCredentialStore _credentialStore;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IOptionsMonitor<CopilotOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GitHubOAuthService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubOAuthService"/> class.
    /// </summary>
    /// <param name="credentialStore">The credential store.</param>
    /// <param name="dataProtectionProvider">The data protection provider.</param>
    /// <param name="options">The options.</param>
    /// <param name="httpClientFactory">The http client factory.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public GitHubOAuthService(
        ICopilotCredentialStore credentialStore,
        IDataProtectionProvider dataProtectionProvider,
        IOptionsMonitor<CopilotOptions> options,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        ILogger<GitHubOAuthService> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _credentialStore = credentialStore;
        _dataProtectionProvider = dataProtectionProvider;
        _options = options;
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Gets authorization url.
    /// </summary>
    /// <param name="callbackUrl">The callback url.</param>
    /// <param name="returnUrl">The return url.</param>
    public string GetAuthorizationUrl(string callbackUrl, string returnUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackUrl);

        var settings = _options.CurrentValue;

        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            throw new InvalidOperationException("GitHub OAuth Client ID is not configured. Please configure Copilot settings.");
        }

        var scopes = string.Join(" ", settings.Scopes ?? ["user:email", "read:org"]);

        var state = returnUrl ?? string.Empty;
        var httpContext = _httpContextAccessor?.HttpContext;

        if (httpContext != null)
        {
            state = Guid.NewGuid().ToString("N");

            var stateProtector = _dataProtectionProvider.CreateProtector(StateProtectorPurpose);
            var statePayload = JsonSerializer.Serialize(new GitHubOAuthState
            {
                Nonce = state,
                ReturnUrl = returnUrl,
                ExpiresUtc = _timeProvider.GetUtcNow().AddMinutes(10),
            });
            var protectedState = stateProtector.Protect(statePayload);

            httpContext.Response.Cookies.Append(StateCookieName, protectedState, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = httpContext.Request.IsHttps,
                Expires = _timeProvider.GetUtcNow().AddMinutes(10),
            });
        }

        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["client_id"] = settings.ClientId;
        queryParams["redirect_uri"] = callbackUrl;
        queryParams["scope"] = scopes;
        queryParams["state"] = state;

        return $"https://github.com/login/oauth/authorize?{queryParams}";
    }

    /// <summary>
    /// Validates the callback state and extracts the safe return URL.
    /// </summary>
    /// <param name="state">The callback state.</param>
    /// <param name="validatedReturnUrl">The validated return URL when the state is valid.</param>
    /// <returns><see langword="true"/> when the state contains a safe return URL; otherwise, <see langword="false"/>.</returns>
    public bool TryValidateCallbackState(string state, out string validatedReturnUrl)
    {
        var httpContext = _httpContextAccessor?.HttpContext;

        if (httpContext == null)
        {
            if (string.Equals(state, "__popup__", StringComparison.Ordinal))
            {
                validatedReturnUrl = state;

                return true;
            }

            if (IsLocalUrl(state))
            {
                validatedReturnUrl = state;

                return true;
            }

            validatedReturnUrl = null;

            return false;
        }

        if (string.IsNullOrWhiteSpace(state) || !httpContext.Request.Cookies.TryGetValue(StateCookieName, out var protectedState) || string.IsNullOrWhiteSpace(protectedState))
        {
            validatedReturnUrl = null;

            return false;
        }

        try
        {
            var stateProtector = _dataProtectionProvider.CreateProtector(StateProtectorPurpose);
            var payload = JsonSerializer.Deserialize<GitHubOAuthState>(stateProtector.Unprotect(protectedState));

            if (payload == null ||
                !string.Equals(payload.Nonce, state, StringComparison.Ordinal) ||
                payload.ExpiresUtc < _timeProvider.GetUtcNow())
            {
                validatedReturnUrl = null;

                return false;
            }

            if (string.Equals(payload.ReturnUrl, "__popup__", StringComparison.Ordinal) || IsLocalUrl(payload.ReturnUrl))
            {
                validatedReturnUrl = payload.ReturnUrl;

                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GitHub OAuth callback state validation failed.");
        }

        validatedReturnUrl = null;

        return false;
    }

    /// <summary>
    /// Exchanges code for token.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<GitHubOAuthCredential> ExchangeCodeForTokenAsync(
        string code,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var settings = _options.CurrentValue;

        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new InvalidOperationException("GitHub OAuth credentials are not configured. Please configure Copilot settings.");
        }

        // Exchange authorization code for access token.
        var httpClient = _httpClientFactory.CreateClient(CopilotOrchestrator.HttpClientName);

        var tokenRequest = new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId,
            ["client_secret"] = settings.ClientSecret,
            ["code"] = code
        };

        using var tokenRequestMessage = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
        {
            Content = JsonContent.Create(tokenRequest),
        };
        tokenRequestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        tokenRequestMessage.Headers.UserAgent.ParseAdd("CrestApps-Core-Copilot/1.0");

        var tokenResponse = await httpClient.SendAsync(tokenRequestMessage, cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();

        var tokenData = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

        var accessToken = tokenData.GetProperty("access_token").GetString();

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("Failed to retrieve access token from GitHub.");
        }

        // Get user information from GitHub.
        using var userRequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
        userRequestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        userRequestMessage.Headers.UserAgent.ParseAdd("CrestApps-Core-Copilot/1.0");
        userRequestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var userResponse = await httpClient.SendAsync(userRequestMessage, cancellationToken);
        userResponse.EnsureSuccessStatusCode();

        var userData = await userResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var username = userData.GetProperty("login").GetString();

        // Protect tokens.
        var tokenProtector = _dataProtectionProvider.CreateProtector(ProtectorPurpose);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var credential = new CopilotProtectedCredential
        {
            GitHubUsername = username,
            ProtectedAccessToken = tokenProtector.Protect(accessToken),
            ProtectedRefreshToken = null, // GitHub OAuth doesn't provide refresh tokens
            ExpiresAt = null, // GitHub tokens don't have explicit expiration
            UpdatedUtc = now,
        };

        await _credentialStore.SaveProtectedCredentialAsync(userId, credential, cancellationToken);

        return new GitHubOAuthCredential
        {
            UserId = userId,
            GitHubUsername = username,
            ExpiresAt = null,
            UpdatedUtc = now,
        };
    }

    /// <summary>
    /// Gets credential.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<GitHubOAuthCredential> GetCredentialAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var credential = await _credentialStore.GetProtectedCredentialAsync(userId, cancellationToken);

        if (credential is null || string.IsNullOrEmpty(credential.ProtectedAccessToken))
        {
            return null;
        }

        return new GitHubOAuthCredential
        {
            UserId = userId,
            GitHubUsername = credential.GitHubUsername,
            ExpiresAt = credential.ExpiresAt,
            UpdatedUtc = credential.UpdatedUtc,
        };
    }

    /// <summary>
    /// Gets the raw protected (encrypted) credentials for the specified user.
    /// These can be stored on an AIProfile entity for reuse across sessions.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<CopilotProtectedCredential> GetProtectedCredentialsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var credential = await _credentialStore.GetProtectedCredentialAsync(userId, cancellationToken);

        if (credential is not null && !string.IsNullOrEmpty(credential.ProtectedAccessToken))
        {
            return credential;
        }

        return null;
    }

    /// <summary>
    /// Gets valid access token.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<string> GetValidAccessTokenAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var credential = await _credentialStore.GetProtectedCredentialAsync(userId, cancellationToken);

        if (credential is null || string.IsNullOrEmpty(credential.ProtectedAccessToken))
        {
            return null;
        }

        var protector = _dataProtectionProvider.CreateProtector(ProtectorPurpose);

        try
        {
            var accessToken = protector.Unprotect(credential.ProtectedAccessToken);

            return accessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unprotect access token for user {UserId}", userId);

            return null;
        }
    }

    /// <summary>
    /// Unprotects a stored access token from a <see cref="CopilotSessionMetadata"/>,
    /// typically stored on an <see cref="AI.Models.AIProfile"/> entity.
    /// Returns <c>null</c> if the token is missing, expired, or cannot be unprotected.
    /// </summary>
    internal string UnprotectAccessToken(CopilotSessionMetadata metadata)
    {
        if (metadata is null || string.IsNullOrEmpty(metadata.ProtectedAccessToken))
        {
            return null;
        }

        var protector = _dataProtectionProvider.CreateProtector(ProtectorPurpose);

        try
        {
            return protector.Unprotect(metadata.ProtectedAccessToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unprotect access token from profile metadata for user {Username}", metadata.GitHubUsername);

            return null;
        }
    }

    /// <summary>
    /// Disconnects the operation.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task DisconnectAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await _credentialStore.ClearCredentialAsync(userId, cancellationToken);
    }

    /// <summary>
    /// Determines whether authenticated.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<bool> IsAuthenticatedAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var credential = await GetCredentialAsync(userId, cancellationToken);

        if (credential == null)
        {
            return false;
        }

        // Check if token is not expired.

        if (credential.ExpiresAt.HasValue && credential.ExpiresAt.Value < _timeProvider.GetUtcNow().UtcDateTime)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Lists models.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<IReadOnlyCollection<CopilotModelInfo>> ListModelsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var accessToken = await GetValidAccessTokenAsync(userId, cancellationToken);

        if (string.IsNullOrEmpty(accessToken))
        {
            return [];
        }

        try
        {
            // Use the Copilot SDK to list models. The SDK's CLI server handles
            // authentication and returns only models the user actually has access to.
            var clientOptions = new CopilotClientOptions
            {
                GitHubToken = accessToken,
                Logger = _logger,
            };

            await using var client = new CopilotClient(clientOptions);

            var models = await client.ListModelsAsync(cancellationToken);

            return models
                            .Where(m => !string.IsNullOrEmpty(m.Id))
                            .Select(m => new CopilotModelInfo
                            {
                                Id = m.Id,
                                Name = !string.IsNullOrEmpty(m.Name) ? m.Name : m.Id,
                                CostMultiplier = m.Billing?.Multiplier ?? 0,
                            })
                            .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error listing Copilot models for user {UserId}", userId);

            return [];
        }
    }

    private static bool IsLocalUrl(string url)
    {
        return !string.IsNullOrEmpty(url) &&
            ((url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))) ||
            (url[0] == '~' && url.Length > 1 && url[1] == '/'));
    }

    private sealed class GitHubOAuthState
    {
        public string Nonce { get; set; }

        public string ReturnUrl { get; set; }

        public DateTimeOffset ExpiresUtc { get; set; }
    }
}
