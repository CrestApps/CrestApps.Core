using CrestApps.Core.AI.Clients;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI;

public sealed class AIOptions
{
    private readonly Dictionary<string, Type> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AIProfileProviderEntry> _profileSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AIDeploymentProviderEntry> _deployments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AIProviderConnectionOptionsEntry> _connectionSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AITemplateSourceEntry> _templateSources = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, Type> Clients
    {
        get
        {
            return _clients;
        }
    }

    public IReadOnlyDictionary<string, AIProfileProviderEntry> ProfileSources
    {
        get
        {
            return _profileSources;
        }
    }

    public IReadOnlyDictionary<string, AIDeploymentProviderEntry> Deployments
    {
        get
        {
            return _deployments;
        }
    }

    public IReadOnlyDictionary<string, AIProviderConnectionOptionsEntry> ConnectionSources
    {
        get
        {
            return _connectionSources;
        }
    }

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

    public void AddProfileSource(string name, string providerName, Action<AIProfileProviderEntry> configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!_profileSources.TryGetValue(name, out var entry))
        {
            entry = new AIProfileProviderEntry(providerName);
        }

        if (configure != null)
        {
            configure(entry);
        }

        if (string.IsNullOrEmpty(entry.DisplayName))
        {
            entry.DisplayName = new LocalizedString(providerName, providerName);
        }

        _profileSources[name] = entry;
    }

    public void AddDeploymentProvider(string providerName, Action<AIDeploymentProviderEntry> configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(providerName);
        if (!_deployments.TryGetValue(providerName, out var entry))
        {
            entry = new AIDeploymentProviderEntry();
        }

        if (configure != null)
        {
            configure(entry);
        }

        if (string.IsNullOrEmpty(entry.DisplayName))
        {
            entry.DisplayName = new LocalizedString(providerName, providerName);
        }

        _deployments[providerName] = entry;
    }

    public void AddConnectionSource(string providerName, Action<AIProviderConnectionOptionsEntry> configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(providerName);
        if (!_connectionSources.TryGetValue(providerName, out var entry))
        {
            entry = new AIProviderConnectionOptionsEntry(providerName);
        }

        if (configure != null)
        {
            configure(entry);
        }

        if (string.IsNullOrEmpty(entry.DisplayName))
        {
            entry.DisplayName = new LocalizedString(providerName, providerName);
        }

        _connectionSources[providerName] = entry;
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
