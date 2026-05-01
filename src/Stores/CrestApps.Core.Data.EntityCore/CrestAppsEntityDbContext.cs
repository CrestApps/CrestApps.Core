using CrestApps.Core.Data.EntityCore.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Data.EntityCore;

/// <summary>
/// The framework's Entity Framework Core <see cref="DbContext"/> for CrestApps stores.
/// </summary>
/// <remarks>
/// This type is not intended to be subclassed by consumers. To contribute additional
/// model configuration, implement <see cref="ICrestAppsModelConfigurer"/> and register
/// it with the dependency injection container; configurers are invoked from
/// <see cref="OnModelCreating(ModelBuilder)"/> in registration order.
/// </remarks>
public sealed class CrestAppsEntityDbContext : DbContext
{
    private readonly EntityCoreDataStoreOptions _options;
    private readonly IEnumerable<ICrestAppsModelConfigurer> _modelConfigurers;

    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsEntityDbContext"/> class.
    /// </summary>
    /// <param name="options">The Entity Framework Core context options.</param>
    /// <param name="storeOptions">The CrestApps Entity Framework Core store options.</param>
    /// <param name="modelConfigurers">The additional model configurers to apply.</param>
    public CrestAppsEntityDbContext(
        DbContextOptions<CrestAppsEntityDbContext> options,
        IOptions<EntityCoreDataStoreOptions> storeOptions,
        IEnumerable<ICrestAppsModelConfigurer> modelConfigurers)
        : base(options)
    {
        _options = storeOptions.Value;
        _modelConfigurers = modelConfigurers ?? [];
    }

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> for <see cref="CatalogRecord"/> rows.
    /// </summary>
    public DbSet<CatalogRecord> CatalogRecords => Set<CatalogRecord>();

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> for <see cref="AIChatSessionRecord"/> rows.
    /// </summary>
    public DbSet<AIChatSessionRecord> AIChatSessionRecords => Set<AIChatSessionRecord>();

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> for <see cref="AIChatSessionExtractedDataStoreRecord"/> rows.
    /// </summary>
    public DbSet<AIChatSessionExtractedDataStoreRecord> AIChatSessionExtractedDataRecords => Set<AIChatSessionExtractedDataStoreRecord>();

    /// <summary>
    /// Configures the CrestApps Entity Framework Core model.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var tablePrefix = _options.TablePrefix ?? string.Empty;

        modelBuilder.Entity<CatalogRecord>(entity =>
        {
            entity.ToTable($"{tablePrefix}CatalogRecords");
            entity.HasKey(x => new { x.EntityType, x.ItemId });
            entity.Property(x => x.EntityType).IsRequired();
            entity.Property(x => x.ItemId).HasMaxLength(26);
            entity.Property(x => x.Name);
            entity.Property(x => x.DisplayText);
            entity.Property(x => x.Source);
            entity.Property(x => x.SessionId);
            entity.Property(x => x.ChatInteractionId);
            entity.Property(x => x.ReferenceId);
            entity.Property(x => x.ReferenceType);
            entity.Property(x => x.AIDocumentId);
            entity.Property(x => x.UserId);
            entity.Property(x => x.Type);
            entity.Property(x => x.Payload).IsRequired();

            entity.HasIndex(x => new { x.EntityType, x.Name });
            entity.HasIndex(x => new { x.EntityType, x.Source });
            entity.HasIndex(x => new { x.EntityType, x.SessionId });
            entity.HasIndex(x => new { x.EntityType, x.ChatInteractionId });
            entity.HasIndex(x => new { x.EntityType, x.ReferenceId, x.ReferenceType });
            entity.HasIndex(x => new { x.EntityType, x.AIDocumentId });
            entity.HasIndex(x => new { x.EntityType, x.UserId, x.Name });
            entity.HasIndex(x => new { x.EntityType, x.Type });

            if (_options.EnforceNamedSourceUniqueness)
            {
                entity
                    .HasIndex(x => new { x.EntityType, x.Source, x.Name })
                    .IsUnique();
            }
        });

        modelBuilder.Entity<AIChatSessionRecord>(entity =>
        {
            entity.ToTable($"{tablePrefix}AIChatSessions");
            entity.HasKey(x => x.SessionId);
            entity.Property(x => x.SessionId).HasMaxLength(26);
            entity.Property(x => x.ProfileId);
            entity.Property(x => x.Title);
            entity.Property(x => x.UserId);
            entity.Property(x => x.ClientId);
            entity.Property(x => x.Payload).IsRequired();

            entity.HasIndex(x => x.ProfileId);
            entity.HasIndex(x => x.LastActivityUtc);
        });

        modelBuilder.Entity<AIChatSessionExtractedDataStoreRecord>(entity =>
        {
            entity.ToTable($"{tablePrefix}AIChatSessionExtractedData");
            entity.HasKey(x => x.SessionId);
            entity.Property(x => x.SessionId).HasMaxLength(26);
            entity.Property(x => x.ProfileId).IsRequired();
            entity.Property(x => x.Payload).IsRequired();

            entity.HasIndex(x => x.ProfileId);
            entity.HasIndex(x => x.SessionStartedUtc);
            entity.HasIndex(x => x.UpdatedUtc);
        });

        foreach (var configurer in _modelConfigurers)
        {
            configurer.Configure(modelBuilder, _options);
        }
    }
}
