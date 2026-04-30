using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CrestApps.Core.Data.EntityCore.Services;

/// <summary>
/// Cache key factory that incorporates <see cref="CrestAppsOptionsExtension"/> values
/// (which influence the compiled EF Core model) so that distinct configurations produce
/// distinct compiled models within the same process.
/// </summary>
internal sealed class CrestAppsModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        var extension = context.GetService<IDbContextOptions>()
            .FindExtension<CrestAppsOptionsExtension>();

        return (
            context.GetType(),
            extension?.TablePrefix ?? string.Empty,
            extension?.EnforceNamedSourceUniqueness ?? false,
            designTime);
    }
}
