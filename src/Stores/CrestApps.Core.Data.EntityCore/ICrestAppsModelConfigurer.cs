using Microsoft.EntityFrameworkCore;

namespace CrestApps.Core.Data.EntityCore;

/// <summary>
/// Extension point for contributing additional Entity Framework Core model
/// configuration to <see cref="CrestAppsEntityDbContext"/> without subclassing
/// or referencing the framework <see cref="DbContext"/> type directly.
/// </summary>
/// <remarks>
/// Implementations are resolved from the dependency injection container during
/// <see cref="DbContext.OnModelCreating(ModelBuilder)"/> and invoked in
/// registration order. Use this contract to add owned entities, indexes, value
/// converters, or query filters for downstream feature modules.
/// </remarks>
public interface ICrestAppsModelConfigurer
{
    /// <summary>
    /// Applies model configuration to the supplied <see cref="ModelBuilder"/>.
    /// </summary>
    /// <param name="modelBuilder">The Entity Framework Core model builder.</param>
    /// <param name="options">The store options that control naming conventions (e.g., table prefix).</param>
    void Configure(ModelBuilder modelBuilder, EntityCoreDataStoreOptions options);
}
