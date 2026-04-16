using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrestApps.Core.Blazor.Web.Services;

public sealed class SiteSettingsStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private static readonly Dictionary<string, string> _flatKeyMigrations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GeneralSettings"] = "GeneralAISettings",
        ["DefaultOrchestrator"] = "DefaultOrchestratorSettings",
        ["DefaultDeployments"] = "DefaultAIDeploymentSettings",
        ["Memory"] = "AIMemorySettings",
        ["InteractionDocuments"] = "InteractionDocumentSettings",
        ["DataSources"] = "AIDataSourceSettings",
        ["ChatInteraction"] = "ChatInteractionSettings",
        ["ChatInteractionMemory"] = "MemoryMetadata",
        ["Copilot"] = "CopilotSettings",
        ["Anthropic"] = "ClaudeSettings",
    };

    private static readonly Dictionary<string, Dictionary<string, string>> _nestedKeyMigrations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MCP"] = new(StringComparer.OrdinalIgnoreCase) { ["Server"] = "McpServerOptions" },
        ["Admin"] = new(StringComparer.OrdinalIgnoreCase) { ["Pagination"] = "PaginationSettings", ["Widget"] = "AIChatAdminWidgetSettings" },
        ["A2A"] = new(StringComparer.OrdinalIgnoreCase) { ["Host"] = "A2AHostOptions" },
    };

    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly string _filePath;
    private readonly JsonObject _root;

    public SiteSettingsStore(string appDataPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataPath);
        _filePath = Path.Combine(appDataPath, "site-settings.json");
        _root = LoadFromDisk();
        MigrateKeys(_root);
    }

    public T Get<T>() where T : class, new() => Get<T>(typeof(T).Name);

    public T Get<T>(string key) where T : class, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_stateLock)
        {
            if (_root.TryGetPropertyValue(key, out var node) && node is not null)
                return node.Deserialize<T>(_jsonOptions) ?? new T();
            return new T();
        }
    }

    public void Set<T>(T value) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_stateLock) { _root[typeof(T).Name] = JsonSerializer.SerializeToNode(value, _jsonOptions); }
    }

    public void Set<T>(Action<T> configure) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(configure);
        var current = Get<T>();
        configure(current);
        Set(current);
    }

    public async Task SaveChangesAsync()
    {
        string json;
        lock (_stateLock) { json = _root.ToJsonString(_jsonOptions); }
        await _writeLock.WaitAsync();
        try
        {
            var directoryPath = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directoryPath)) Directory.CreateDirectory(directoryPath);
            var tempPath = _filePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally { _writeLock.Release(); }
    }

    private JsonObject LoadFromDisk()
    {
        if (!File.Exists(_filePath)) return [];
        try
        {
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json)) return [];
            return JsonNode.Parse(json) as JsonObject ?? [];
        }
        catch (Exception ex)
        {
            var crashPath = Path.Combine(Path.GetDirectoryName(_filePath) ?? ".", "site-settings-load-error.log");
            try { File.WriteAllText(crashPath, $"[{DateTime.UtcNow:O}] Failed to load {_filePath}:{Environment.NewLine}{ex}"); } catch { }
            return [];
        }
    }

    private static void MigrateKeys(JsonObject root)
    {
        foreach (var (parentKey, children) in _nestedKeyMigrations)
        {
            if (root[parentKey] is not JsonObject parentNode) continue;
            foreach (var (childKey, newKey) in children)
            {
                if (root.ContainsKey(newKey)) continue;
                if (parentNode[childKey] is JsonNode childNode) root[newKey] = childNode.DeepClone();
            }
            root.Remove(parentKey);
        }
        foreach (var (oldKey, newKey) in _flatKeyMigrations)
        {
            if (root.ContainsKey(newKey)) continue;
            if (root[oldKey] is JsonNode node) { root[newKey] = node.DeepClone(); root.Remove(oldKey); }
        }
    }
}
