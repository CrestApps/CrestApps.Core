using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace CrestApps.Core.Startup.Shared.Services;

/// <summary>
/// Resolves and persists the currently selected sample-host server target for a client sample application.
/// </summary>
public sealed class SampleServerSelectionService
{
    private readonly string _cookieName;
    private readonly string _defaultServerName;
    private readonly List<ConfiguredServerEndpoint> _servers;

    /// <summary>
    /// Initializes a new instance of the <see cref="SampleServerSelectionService"/> class.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="sectionName">The root configuration section containing the server definitions.</param>
    /// <param name="cookieName">The cookie name used to persist the current selection.</param>
    public SampleServerSelectionService(
        IConfiguration configuration,
        string sectionName,
        string cookieName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(cookieName);

        _cookieName = cookieName;
        _servers = LoadServers(configuration.GetSection(sectionName));
        _defaultServerName = configuration[$"{sectionName}:DefaultServer"] ?? string.Empty;
    }

    /// <summary>
    /// Returns the configured server targets for the current sample application.
    /// </summary>
    /// <returns>The configured sample-host server targets.</returns>
    public IReadOnlyList<ConfiguredServerEndpoint> GetServers()
    {
        return _servers;
    }

    /// <summary>
    /// Resolves the currently selected server for the supplied HTTP context.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>The active server target.</returns>
    public ConfiguredServerEndpoint GetCurrent(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (_servers.Count == 0)
        {
            throw new InvalidOperationException("No sample server endpoints are configured.");
        }

        var cookieValue = httpContext.Request.Cookies[_cookieName];

        if (!string.IsNullOrWhiteSpace(cookieValue) && TryGetServer(cookieValue, out var selectedServer))
        {
            return selectedServer;
        }

        if (!string.IsNullOrWhiteSpace(_defaultServerName) && TryGetServer(_defaultServerName, out var defaultServer))
        {
            return defaultServer;
        }

        return _servers[0];
    }

    /// <summary>
    /// Attempts to resolve a configured server target by name.
    /// </summary>
    /// <param name="serverName">The configured server name.</param>
    /// <param name="server">The resolved server target when found.</param>
    /// <returns><see langword="true"/> when the server exists; otherwise <see langword="false"/>.</returns>
    public bool TryGetServer(string serverName, out ConfiguredServerEndpoint server)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);

        server = _servers.FirstOrDefault(candidate => string.Equals(candidate.Name, serverName, StringComparison.OrdinalIgnoreCase))!;

        return server is not null;
    }

    /// <summary>
    /// Persists the selected server target to a response cookie.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="serverName">The configured server name to persist.</param>
    public void SetCurrent(HttpContext httpContext, string serverName)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);

        if (!TryGetServer(serverName, out _))
        {
            throw new InvalidOperationException($"The sample server '{serverName}' is not configured.");
        }

        httpContext.Response.Cookies.Append(_cookieName, serverName, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = httpContext.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(30),
        });
    }

    private static List<ConfiguredServerEndpoint> LoadServers(IConfiguration section)
    {
        var servers = new List<ConfiguredServerEndpoint>();

        var serversSection = section.GetSection("Servers");

        foreach (var child in serversSection.GetChildren())
        {
            var endpoint = child["Endpoint"];

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                continue;
            }

            servers.Add(new ConfiguredServerEndpoint
            {
                Name = child.Key,
                DisplayName = string.IsNullOrWhiteSpace(child["DisplayName"]) ? child.Key : child["DisplayName"],
                Endpoint = endpoint,
                ApiKey = child["ApiKey"],
            });
        }

        if (servers.Count > 0)
        {
            return servers;
        }

        var legacyEndpoint = section["Endpoint"];

        if (string.IsNullOrWhiteSpace(legacyEndpoint))
        {
            return [];
        }

        return
        [
            new ConfiguredServerEndpoint
            {
                Name = "Default",
                DisplayName = "Default",
                Endpoint = legacyEndpoint,
                ApiKey = section["ApiKey"],
            },
        ];
    }
}

/// <summary>
/// Describes a single configured sample-host server target.
/// </summary>
public sealed class ConfiguredServerEndpoint
{
    /// <summary>
    /// Gets or sets the configuration key for the server target.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the friendly label shown in the sample client UI.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base endpoint used by the client sample.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional API key sent to the target server.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
