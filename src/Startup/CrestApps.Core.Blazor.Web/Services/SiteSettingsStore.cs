using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrestApps.Core.Blazor.Web.Services;

/// <summary>
/// In-memory store for admin-managed site settings backed by
/// <c>App_Data/site-settings.json</c>. The entire JSON document is loaded into
/// memory at startup. Reads are served from the in-memory copy (no file I/O),
/// and writes update the in-memory copy immediately. Call
/// <see cref="SaveChangesAsync"/> to persist the current state to disk in a
/// single atomic operation.
///
/// Each settings type is stored under a key derived from its class name
/// (e.g. <c>GeneralAISettings</c>, <c>PaginationSettings</c>). If a section
/// does not yet exist, <see cref="Get{T}()"/> returns a default instance.
/// </summary>
public sealed class SiteSettingsStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    // Maps old JSON keys to new typeof(T).Name keys so that existing
    // site-settings.json files written by prior versions are read correctly.
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

    // Nested keys that must be extracted from a parent object and promoted to
    // top-level keys.  Format: { parentKey: { childKey: newTopLevelKey } }.
    private static readonly Dictionary<string, Dictionary<string, string>> _nestedKeyMigrations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MCP"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Server"] = "McpServerOptions",
        },
        ["Admin"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Pagination"] = "PaginationSettings",
            ["Widget"] = "AIChatAdminWidgetSettings",
        },
        ["A2A"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Host"] = "A2AHostOptions",
        },
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

    /// <summary>
    /// Reads a settings section from the in-memory store. The section key is
    /// the simple class name of <typeparamref name="T"/>. Returns a new
    /// <typeparamref name="T"/> instance when the section does not exist.
    /// </summary>
    public T Get<T>() where T : class, new()
    {
        return Get<T>(typeof(T).Name);
    }

    /// <summary>
    /// Reads a settings section from the in-memory store using an explicit key.
    /// Returns a new <typeparamref name="T"/> instance when the key does not
    /// exist. This is useful when the JSON key differs from the class name
    /// (e.g. reading a framework options type from a settings key).
    /// </summary>
    public T Get<T>(string key) where T : class, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_stateLock)
        {
            if (_root.TryGetPropertyValue(key, out var node) && node is not null)
            {
                return node.Deserialize<T>(_jsonOptions) ?? new T();
            }

            return new T();
        }
    }

    /// <summary>
    /// Updates a settings section in the in-memory store. The section key is
    /// the simple class name of <typeparamref name="T"/>. This does not write
    /// to disk — call <see cref="SaveChangesAsync"/> when all updates are
    /// complete.
    /// </summary>
    public void Set<T>(T value) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);

        var key = typeof(T).Name;

        lock (_stateLock)
        {
            _root[key] = JsonSerializer.SerializeToNode(value, _jsonOptions);
        }
    }

    /// <summary>
    /// Reads a settings section, applies <paramref name="configure"/>, then
    /// writes the modified instance back to the in-memory store. This does not
    /// write to disk — call <see cref="SaveChangesAsync"/> when all updates are
    /// complete.
    /// </summary>
    public void Set<T>(Action<T> configure) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(configure);

        var current = Get<T>();
        configure(current);
        Set(current);
    }

    /// <summary>
    /// Persists the current in-memory state to <c>App_Data/site-settings.json</c>
    /// in a single atomic file write. Uses a temp-file-then-rename strategy so
    /// the target file is never partially written.
    /// </summary>
    public async Task SaveChangesAsync()
    {
        string json;
        lock (_stateLock)
        {
            json = _root.ToJsonString(_jsonOptions);
        }

        await _writeLock.WaitAsync();
        try
        {
            var directoryPath = Path.GetDirectoryName(_filePath);

            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            // Write to a temp file first, then atomically replace the target.
            var tempPath = _filePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private JsonObject LoadFromDisk()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            return JsonNode.Parse(json) as JsonObject ?? [];
        }
        catch (Exception ex)
        {
            // Log the parse failure so the operator can investigate, then start
            // with an empty settings bag instead of crashing the host.
            var crashPath = Path.Combine(Path.GetDirectoryName(_filePath) ?? ".", "site-settings-load-error.log");

            try { File.WriteAllText(crashPath, $"[{DateTime.UtcNow:O}] Failed to load {_filePath}:{Environment.NewLine}{ex}"); } catch { }

            return [];
        }
    }

    /// <summary>
    /// Migrates old JSON keys written by prior versions to the new
    /// <c>typeof(T).Name</c>-based keys. This handles both simple renames
    /// (e.g. <c>GeneralSettings</c> → <c>GeneralAISettings</c>) and nested
    /// extractions (e.g. <c>MCP.Server</c> → <c>McpServerOptions</c>).
    /// </summary>
    private static void MigrateKeys(JsonObject root)
    {
        // Extract nested keys first (e.g. Admin.Widget → AIChatAdminWidgetSettings).
        foreach (var (parentKey, children) in _nestedKeyMigrations)
        {
            if (root[parentKey] is not JsonObject parentNode)
            {
                continue;
            }

            foreach (var (childKey, newKey) in children)
            {
                if (root.ContainsKey(newKey))
                {
                    continue;
                }

                if (parentNode[childKey] is JsonNode childNode)
                {
                    root[newKey] = childNode.DeepClone();
                }
            }

            // Remove the parent if all its children have been extracted.
            root.Remove(parentKey);
        }

        // Rename flat keys (e.g. GeneralSettings → GeneralAISettings).
        foreach (var (oldKey, newKey) in _flatKeyMigrations)
        {
            if (root.ContainsKey(newKey))
            {
                continue;
            }

            if (root[oldKey] is JsonNode node)
            {
                root[newKey] = node.DeepClone();
                root.Remove(oldKey);
            }
        }
    }
}
