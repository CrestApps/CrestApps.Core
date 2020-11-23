using Microsoft.EntityFrameworkCore;
using OrchardCore.Environment.Shell;
using CrestApps.Data.Core.Abstraction;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
namespace CrestApps.Data.Entity
{
    public class TenantEntityReadRepository<TEntity, TKeyType> : IReadRepository<TEntity, TKeyType>
         where TEntity : class, IReadModel, ITenantModel
         where TKeyType : struct
    {
        protected readonly DbContext Context;
        protected readonly ShellSettings ShellSettings;
        protected readonly DbSet<TEntity> DbSet;

        public TenantEntityReadRepository(DbContext context, ShellSettings shellSettings)
        {
            Context = context;
            ShellSettings = shellSettings;
            DbSet = context.Set<TEntity>();
        }


        public virtual async Task<TEntity> SingleOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await Query().SingleOrDefaultAsync(predicate, cancellationToken);
        }

        public virtual async Task<TEntity> GetAsync(TKeyType id, CancellationToken cancellationToken = default)
        {
            Guid tenantId = CurrentTenantId();

            TEntity entity = await DbSet.FindAsync(id, cancellationToken);

            if (entity == null || entity.TenantId != tenantId)
            {
                return null;
            }

            return entity;
        }

        public virtual async Task<TEntity> FirstAsync(TKeyType id, CancellationToken cancellationToken = default)
        {
            return await GetAsync(id, cancellationToken) ?? throw new ModelNotFoundException();
        }

        public virtual async Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await Query().ToListAsync(cancellationToken);
        }

        public virtual async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await Query().AnyAsync(predicate, cancellationToken);
        }



        public virtual TEntity Get(TKeyType id)
        {
            Guid tenantId = CurrentTenantId();

            TEntity entity = DbSet.Find(id);

            if (entity == null || entity.TenantId != tenantId)
            {
                return null;
            }

            return entity;
        }

        public virtual TEntity First(TKeyType id)
        {
            return Get(id) ?? throw new ModelNotFoundException();
        }

        public virtual IEnumerable<TEntity> GetAll()
        {
            return Query().ToList();
        }

        public virtual bool Any(Expression<Func<TEntity, bool>> predicate)
        {
            return Query().Any(predicate);
        }

        public virtual IQueryable<TEntity> Where(Expression<Func<TEntity, bool>> predicate)
        {
            return Query().Where(predicate);
        }

        protected Guid CurrentTenantId()
        {
            return new Guid(ShellSettings["Identifier"] ?? ShellSettings.Name);
        }

        public virtual TEntity SingleOrDefault(Expression<Func<TEntity, bool>> predicate)
        {
            return Query().SingleOrDefault(predicate);
        }

        public virtual IQueryable<TEntity> Query(QueryOptions options = null)
        {
            Guid tenantId = CurrentTenantId();

            var query = DbSet.Where(x => x.TenantId == tenantId);

            if (options != null && !options.IsTrackable)
            {
                return query.AsNoTracking();
            }

            return query;
        }
    }


    public class TenantEntityReadRepository<TEntity> : TenantEntityReadRepository<TEntity, int>
        where TEntity : class, IReadModel, ITenantModel
    {
        public TenantEntityReadRepository(DbContext context, ShellSettings shellSettings)
            : base(context, shellSettings)
        {
        }
    }
}
