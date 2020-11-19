using CrestApps.Data.Core.Abstraction;
using Microsoft.EntityFrameworkCore;
using OrchardCore.Environment.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CrestApps.Data.Entity
{
    public class TenantEntityRepository<TEntity, TKeyType> : TenantEntityReadRepository<TEntity, TKeyType>, IRepository<TEntity, TKeyType>
         where TEntity : class, IWriteModel, ITenantModel
         where TKeyType : struct
    {

        public TenantEntityRepository(ApplicationContext context, ShellSettings shellSettings)
            : base(context, shellSettings)
        {
        }

        public virtual TEntity Add(TEntity entity)
        {
            if (entity == null)
            {
                return null;
            }

            entity.TenantId = CurrentTenantId();

            DbSet.Add(entity);

            return entity;
        }

        public virtual IEnumerable<TEntity> Add(IEnumerable<TEntity> entities)
        {
            if (entities == null)
            {
                return null;
            }

            foreach (var entity in entities)
            {
                entity.TenantId = CurrentTenantId();
            }

            DbSet.AddRange(entities);

            return entities;
        }

        public virtual void Remove(TEntity entity)
        {
            if (entity == null)
            {
                return;
            }

            DbSet.Remove(entity);
        }

        public virtual void Remove(IEnumerable<TEntity> entities)
        {
            if (entities == null)
            {
                return;
            }

            DbSet.RemoveRange(entities);
        }

        public virtual void Update(TEntity entity)
        {
            if (entity == null)
            {
                return;
            }

            DbSet.Attach(entity);
            var record = Context.Entry(entity);
            record.State = EntityState.Modified;
        }


        public virtual void Update(IEnumerable<TEntity> entities)
        {
            if (entities == null)
            {
                return;
            }

            DbSet.UpdateRange(entities);
        }


        public virtual void Save()
        {
            Context.SaveChanges();
        }



        public virtual async Task SaveAsync(CancellationToken cancellationToken = default)
        {
            await Context.SaveChangesAsync(cancellationToken);
        }

    }


    public class TenantEntityRepository<TEntity> : TenantEntityRepository<TEntity, int>, IRepository<TEntity>
        where TEntity : class, IWriteModel, ITenantModel
    {
        public TenantEntityRepository(ApplicationContext context, ShellSettings shellSettings)
            : base(context, shellSettings)
        {
        }
    }
}
