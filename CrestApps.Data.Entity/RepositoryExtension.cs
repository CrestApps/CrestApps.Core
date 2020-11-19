using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace CrestApps.Data.Entity
{

    public static class RepositoryExtension
    {
        public static IQueryable<TEntity> With<TEntity, TProperty>(this IQueryable<TEntity> query, params Expression<Func<TEntity, TProperty>>[] relations)
                where TEntity : class
        {
            if (relations == null)
            {
                return query;
            }


            foreach (Expression<Func<TEntity, TProperty>> relation in relations)
            {
                if (relation == null)
                {
                    continue;
                }

                query = query.Include(relation);
            }

            return query;
        }

        public static IIncludeDbQuery<TQuery, TProperty> ThenWith<TQuery, TPropertyCurrent, TProperty>(this IIncludeDbQuery<TQuery, TPropertyCurrent> query,
            Expression<Func<TPropertyCurrent, TProperty>> path)
            where TQuery : class
        {
            var q = query.ThenInclude(path) as IIncludeDbQuery<TQuery, TProperty>;

            return q;
        }


        public static IQueryable<TEntity> ReadOnlyData<TEntity>(this IQueryable<TEntity> query)
            where TEntity : class
        {
            return query.AsNoTracking();
        }




        public static async Task<TEntity> AsFirstOrDefaultAsync<TEntity>(this IQueryable<TEntity> query)
        {
            return await query.FirstOrDefaultAsync();
        }

        public static async Task<TEntity> AsFirstOrDefaultAsync<TEntity>(this IQueryable<TEntity> query, CancellationToken cancellationToken)
        {
            return await query.FirstOrDefaultAsync(cancellationToken);
        }

        public static async Task<TEntity> AsFirstOrDefaultAsync<TEntity>(this IQueryable<TEntity> query, Expression<Func<TEntity, bool>> predict)
        {
            return await query.FirstOrDefaultAsync(predict);
        }

        public static async Task<TEntity> AsFirstOrDefaultAsync<TEntity>(this IQueryable<TEntity> query, Expression<Func<TEntity, bool>> predict, CancellationToken cancellationToken)
        {
            return await query.FirstOrDefaultAsync(predict, cancellationToken);
        }




        public static async Task<TEntity> AsFirstAsync<TEntity>(this IQueryable<TEntity> query)
        {
            return await query.FirstAsync();
        }

        public static async Task<TEntity> AsFirstAsync<TEntity>(this IQueryable<TEntity> query, CancellationToken cancellationToken)
        {
            return await query.FirstAsync(cancellationToken);
        }

        public static async Task<TEntity> AsFirstAsync<TEntity>(this IQueryable<TEntity> query, Expression<Func<TEntity, bool>> predict)
        {
            return await query.FirstAsync(predict);
        }

        public static async Task<TEntity> AsFirstAsync<TEntity>(this IQueryable<TEntity> query, Expression<Func<TEntity, bool>> predict, CancellationToken cancellationToken)
        {
            return await query.FirstAsync(predict, cancellationToken);
        }






        public static async Task<List<TEntity>> AsListAsync<TEntity>(this IQueryable<TEntity> query)
        {
            return await query.ToListAsync();
        }

        public static async Task<List<TEntity>> AsListAsync<TEntity>(this IQueryable<TEntity> query, CancellationToken cancellationToken)
        {
            return await query.ToListAsync(cancellationToken);
        }


        public static async Task<TEntity[]> AsArrayAsync<TEntity>(this IQueryable<TEntity> query)
        {
            return await query.ToArrayAsync();
        }

        public static async Task<TEntity[]> AsArrayAsync<TEntity>(this IQueryable<TEntity> query, CancellationToken cancellationToken)
        {
            return await query.ToArrayAsync(cancellationToken);
        }


        public static async Task<object[]> AsArrayAsync(this IQueryable<object> query)
        {
            return await query.ToArrayAsync();
        }

        public static async Task<object[]> AsArrayAsync(this IQueryable<object> query, CancellationToken cancellationToken)
        {
            return await query.ToArrayAsync(cancellationToken);
        }








        public static async Task<Dictionary<TKey, TEntity>> AsDictionaryAsync<TKey, TEntity>(this IQueryable<TEntity> query, Func<TEntity, TKey> keySelector)
        {
            return await query.ToDictionaryAsync(keySelector);
        }

        public static async Task<Dictionary<TKey, TEntity>> AsDictionaryAsync<TKey, TEntity>(this IQueryable<TEntity> query, Func<TEntity, TKey> keySelector, CancellationToken cancellationToken)
        {
            return await query.ToDictionaryAsync(keySelector, cancellationToken);
        }

        public static async Task<Dictionary<TKey, TElement>> AsDictionaryAsync<TEntity, TKey, TElement>(this IQueryable<TEntity> query, Func<TEntity, TKey> keySelector, Func<TEntity, TElement> elementSelector)
        {
            return await query.ToDictionaryAsync(keySelector, elementSelector);
        }

        public static async Task<Dictionary<TKey, TElement>> AsDictionaryAsync<TEntity, TKey, TElement>(this IQueryable<TEntity> query, Func<TEntity, TKey> keySelector, Func<TEntity, TElement> elementSelector, CancellationToken cancellationToken)
        {
            return await query.ToDictionaryAsync(keySelector, elementSelector, cancellationToken);
        }




        public static async Task<bool> HasAnyAsync<TEntity>(this IQueryable<TEntity> query)
            where TEntity : class
        {
            return await query.AnyAsync();
        }

        public static async Task<bool> HasAnyAsync<TEntity>(this IQueryable<TEntity> query, CancellationToken cancellationToken)
            where TEntity : class
        {
            return await query.AnyAsync(cancellationToken);
        }

        public static async Task<bool> HasAnyAsync<TEntity>(this IQueryable<TEntity> query, Expression<Func<TEntity, bool>> predict)
            where TEntity : class
        {
            return await query.AnyAsync(predict);
        }

        public static async Task<bool> HasAnyAsync<TEntity>(this IQueryable<TEntity> query, Expression<Func<TEntity, bool>> predict, CancellationToken cancellationToken)
            where TEntity : class
        {
            return await query.AnyAsync(predict, cancellationToken);
        }

    }
}