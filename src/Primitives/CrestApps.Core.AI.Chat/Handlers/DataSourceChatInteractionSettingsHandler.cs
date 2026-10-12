using System.Text.Json;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Chat.Handlers;

/// <summary>
/// Represents the data Source Chat Interaction Settings Handler.
/// </summary>
public sealed class DataSourceChatInteractionSettingsHandler : IChatInteractionSettingsHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DataSourceChatInteractionSettingsHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataSourceChatInteractionSettingsHandler"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="logger">The logger.</param>
    public DataSourceChatInteractionSettingsHandler(
        IServiceProvider serviceProvider,
        ILogger<DataSourceChatInteractionSettingsHandler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Updatings the operation.
    /// </summary>
    /// <param name="interaction">The interaction.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task UpdatingAsync(ChatInteraction interaction, JsonElement settings, CancellationToken cancellationToken = default)
    {
        var dataSourceId = GetString(settings, "dataSourceId");

        if (string.IsNullOrWhiteSpace(dataSourceId))
        {
            interaction.Alter<DataSourceMetadata>(metadata => metadata.DataSourceId = null);
            ApplyRagSettings(interaction, settings);

            return;
        }

        var dataSourceCatalog = _serviceProvider.GetService<IAIDataSourceStore>();
        if (dataSourceCatalog == null)
        {
            _logger.LogDebug("Skipping chat interaction data source settings because no AI data source catalog is registered.");

            return;
        }

        var dataSource = await dataSourceCatalog.FindByIdAsync(dataSourceId, cancellationToken);
        if (dataSource == null)
        {
            _logger.LogWarning("Chat interaction data source '{DataSourceId}' was not found while saving settings.", dataSourceId);
            interaction.Alter<DataSourceMetadata>(metadata => metadata.DataSourceId = null);
            ApplyRagSettings(interaction, settings);

            return;
        }

        interaction.Alter<DataSourceMetadata>(metadata =>
        {
            metadata.DataSourceId = dataSource.ItemId;
        });

        ApplyRagSettings(interaction, settings);
    }

    /// <summary>
    /// Updateds the operation.
    /// </summary>
    /// <param name="interaction">The interaction.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task UpdatedAsync(ChatInteraction interaction, JsonElement settings, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    private void ApplyRagSettings(ChatInteraction interaction, JsonElement settings)
    {
        var isInScope = GetBool(settings, "isInScope") ?? false;
        var topNDocuments = GetInt(settings, "topNDocuments");
        var strictness = GetInt(settings, "strictness");
        var filter = GetString(settings, "filter");
        var objectTypes = SplitObjectTypes(GetString(settings, "objectTypes"));
        var isValidFilter = IsValidFilter(filter);

        if (!isValidFilter)
        {
            _logger.LogWarning("Chat interaction '{InteractionId}' kept its previous data source filter because the new filter is not a valid OData filter.", interaction.ItemId);
        }

        interaction.Alter<AIDataSourceRagMetadata>(metadata =>
        {
            metadata.Strictness = strictness;
            metadata.TopNDocuments = topNDocuments;
            metadata.IsInScope = isInScope;
            metadata.ObjectTypes = objectTypes;

            if (isValidFilter)
            {
                metadata.Filter = filter;
            }
        });
    }

    /// <summary>
    /// Determines whether the filter can be stored, with the same <see cref="IODataValidator"/> check the AI
    /// profile editor applies.
    /// </summary>
    /// <param name="filter">The filter as sent by the client.</param>
    /// <returns><see langword="true"/> when the filter is empty, valid, or no validator is registered.</returns>
    /// <remarks>
    /// The chat interaction hub rejects an invalid filter before this handler runs. This check covers a host
    /// that saves the settings another way: the stored filter stays as it was instead of becoming one that
    /// every later search fails on or ignores.
    /// </remarks>
    private bool IsValidFilter(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        var validator = _serviceProvider.GetService<IODataValidator>();

        return validator == null || validator.IsValidFilter(filter);
    }

    /// <summary>
    /// Turns the comma-separated kinds the settings panel carries into the array the metadata stores.
    /// </summary>
    /// <param name="value">The value as typed.</param>
    /// <returns>The kinds, or <see langword="null"/> when nothing was named.</returns>
    /// <remarks>
    /// Naming nothing is not a restriction to nothing: an empty box means every kind, so it stores null
    /// rather than an empty array that a reader could mistake for "none of them".
    /// </remarks>
    private static string[] SplitObjectTypes(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var objectTypes = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        return objectTypes.Length == 0 ? null : objectTypes;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        return null;
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out number))
        {
            return number;
        }

        return null;
    }

    private static bool? GetBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out var value))
        {
            return value;
        }

        return null;
    }
}
