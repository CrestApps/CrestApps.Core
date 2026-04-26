using CrestApps.Core.AI.Clients;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI;

/// <summary>
/// Represents the AI Options.
/// </summary>
public sealed class AIOptions
{
    private readonly Dictionary<string, Type> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AIProfileProviderEntry> _profileSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AIDeploymentProviderEntry> _deployments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AIProviderConnectionOptionsEntry> _connectionSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AITemplateSourceEntry> _templateSources = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets the clients.
    /// </summary>
    public IReadOnlyDictionary<string, Type> Clients
    {
        get
        {
            return _clients;
        }
    }

    /// <summary>
    /// Gets the profile Sources.
    /// </summary>
    public IReadOnlyDictionary<string, AIProfileProviderEntry> ProfileSources
    {
        get
        {
            return _profileSources;
        }
    }

    /// <summary>
    /// Gets the deployments.
    /// </summary>
    public IReadOnlyDictionary<string, AIDeploymentProviderEntry> Deployments
    {
        get
        {
            return _deployments;
        }
    }

    /// <summary>
    /// Gets the connection Sources.
    /// </summary>
    public IReadOnlyDictionary<string, AIProviderConnectionOptionsEntry> ConnectionSources
    {
        get
        {
            return _connectionSources;
        }
    }

    /// <summary>
    /// Gets the template Sources.
    /// </summary>
    public IReadOnlyDictionary<string, AITemplateSourceEntry> TemplateSources
    {
        get
        {
            return _templateSources;
        }
    }

    internal void AddClient<TClient>(string name)
        where TClient : class, IAICompletionClient
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _clients[name] = typeof(TClient);
    }

    public void AddProfileSource(string clientName, Action<AIProfileProviderEntry> configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientName);
        if (!_profileSources.TryGetValue(clientName, out var entry))
        {
            entry = new AIProfileProviderEntry(clientName);
        }

        if (configure != null)
        {
            configure(entry);
        }

        if (string.IsNullOrEmpty(entry.DisplayName))
        {
            entry.DisplayName = new LocalizedString(clientName, clientName);
        }

        _profileSources[clientName] = entry;
    }

    public void AddDeploymentProvider(string clientName, Action<AIDeploymentProviderEntry> configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientName);
        if (!_deployments.TryGetValue(clientName, out var entry))
        {
            entry = new AIDeploymentProviderEntry();
        }

        if (configure != null)
        {
            configure(entry);
        }

        if (string.IsNullOrEmpty(entry.DisplayName))
        {
            entry.DisplayName = new LocalizedString(clientName, clientName);
        }

        _deployments[clientName] = entry;
    }

    public void AddConnectionSource(string clientName, Action<AIProviderConnectionOptionsEntry> configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientName);
        if (!_connectionSources.TryGetValue(clientName, out var entry))
        {
            entry = new AIProviderConnectionOptionsEntry(clientName);
        }

        if (configure != null)
        {
            configure(entry);
        }

        if (string.IsNullOrEmpty(entry.DisplayName))
        {
            entry.DisplayName = new LocalizedString(clientName, clientName);
        }

        _connectionSources[clientName] = entry;
    }

    public void AddTemplateSource(string name, Action<AITemplateSourceEntry> configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!_templateSources.TryGetValue(name, out var entry))
        {
            entry = new AITemplateSourceEntry();
        }

        if (configure != null)
        {
            configure(entry);
        }

        if (string.IsNullOrEmpty(entry.DisplayName))
        {
            entry.DisplayName = new LocalizedString(name, name);
        }

        _templateSources[name] = entry;
    }
}
