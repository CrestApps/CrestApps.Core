using Microsoft.EntityFrameworkCore.Query;

namespace CrestApps.Data.Entity
{
    public interface IIncludeDbQuery<out TEntity, out TProperty> : IIncludableQueryable<TEntity, TProperty>
    {
    }
}
