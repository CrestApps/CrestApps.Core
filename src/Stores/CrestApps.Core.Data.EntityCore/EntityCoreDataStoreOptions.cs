namespace CrestApps.Core.Data.EntityCore;

/// <summary>
/// Configures the Entity Framework Core data store used by CrestApps.Core,
/// including the table name prefix applied to all managed tables.
/// </summary>
public sealed class EntityCoreDataStoreOptions
{
    /// <summary>
    /// Gets or sets the prefix prepended to every table name created by the store.
    /// Defaults to <c>"CA_"</c>.
    /// </summary>
    public string TablePrefix { get; set; } = "CA_";
}
