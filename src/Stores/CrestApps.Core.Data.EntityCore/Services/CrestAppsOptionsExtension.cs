using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Data.EntityCore.Services;

/// <summary>
/// EF Core options extension that carries CrestApps-specific
/// <see cref="EntityCoreDataStoreOptions"/> values which influence the compiled model
/// (currently <see cref="EntityCoreDataStoreOptions.EnforceNamedSourceUniqueness"/> and
/// <see cref="EntityCoreDataStoreOptions.TablePrefix"/>) into EF Core's options pipeline so
/// that <see cref="CrestAppsModelCacheKeyFactory"/> can produce distinct cache keys per
/// configuration.
/// </summary>
internal sealed class CrestAppsOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo _info;

    public CrestAppsOptionsExtension(string tablePrefix, bool enforceNamedSourceUniqueness)
    {
        TablePrefix = tablePrefix ?? string.Empty;
        EnforceNamedSourceUniqueness = enforceNamedSourceUniqueness;
    }

    public string TablePrefix { get; }

    public bool EnforceNamedSourceUniqueness { get; }

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        private readonly CrestAppsOptionsExtension _extension;

        public ExtensionInfo(CrestAppsOptionsExtension extension)
            : base(extension)
        {
            _extension = extension;
        }

        public override bool IsDatabaseProvider => false;

        public override string LogFragment =>
            $"CrestAppsOptions(prefix={_extension.TablePrefix}, enforceNamedSourceUniqueness={_extension.EnforceNamedSourceUniqueness}) ";

        public override int GetServiceProviderHashCode()
            => HashCode.Combine(_extension.TablePrefix, _extension.EnforceNamedSourceUniqueness);

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo info
                && info._extension.TablePrefix == _extension.TablePrefix
                && info._extension.EnforceNamedSourceUniqueness == _extension.EnforceNamedSourceUniqueness;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["CrestApps:TablePrefix"] = _extension.TablePrefix;
            debugInfo["CrestApps:EnforceNamedSourceUniqueness"] = _extension.EnforceNamedSourceUniqueness.ToString();
        }
    }
}
