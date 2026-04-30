namespace CrestApps.Core.Data.EntityCore;

/// <summary>
/// Options for configuring the CrestApps Entity Framework Core data store.
/// </summary>
public sealed class EntityCoreDataStoreOptions
{
    /// <summary>
    /// Gets or sets the prefix applied to every CrestApps-managed table name. Defaults to <c>CA_</c>.
    /// </summary>
    public string TablePrefix { get; set; } = "CA_";

    /// <summary>
    /// When <c>true</c>, the model adds a unique index on
    /// <c>(EntityType, Source, Name)</c> over <see cref="Models.CatalogRecord"/>, enforcing that
    /// named entries within a given <c>EntityType</c> + <c>Source</c> are unique at the database level.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>false</c> to preserve back-compat for existing databases that may contain
    /// duplicates (notably for entity types that do not semantically require uniqueness on
    /// <c>(Source, Name)</c>). Before flipping this to <c>true</c> you must:
    /// <list type="bullet">
    /// <item>Audit existing data and resolve duplicates for the entity types you intend to constrain.</item>
    /// <item>Generate and apply an EF Core migration that adds the new unique index.</item>
    /// </list>
    /// Null-handling for the index follows the underlying database provider's semantics (e.g.,
    /// SQL Server treats multiple nulls as duplicate values in unique indexes; PostgreSQL treats
    /// them as distinct by default).
    /// </remarks>
    public bool EnforceNamedSourceUniqueness { get; set; }
}
