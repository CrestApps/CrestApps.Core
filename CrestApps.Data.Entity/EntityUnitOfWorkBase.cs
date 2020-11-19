using CrestApps.Data.Core.Abstraction;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestApps.Data.Entity
{
    public abstract class EntityUnitOfWorkBase<TDbContext, TUnitOfWork> : IUnitOfWorkBase, IDisposable
            where TDbContext : DbContext
            where TUnitOfWork : class
    {
        private bool Disposed = false;
        protected TDbContext Context { get; private set; }
        protected ILogger Logger { get; private set; }

        protected EntityUnitOfWorkBase(TDbContext context, ILogger<TUnitOfWork> logger)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public virtual int Save()
        {
            Context.SaveChanges();

            return 0;
        }

        public virtual async Task<int> SaveAsync(CancellationToken cancellationToken = default)
        {
            await Context.SaveChangesAsync(cancellationToken);

            return 0;
        }

        public virtual IDatabaseTransaction BeginTransaction()
        {
            return new EntityDatabaseTransaction(Context);
        }

        public async Task TransactionAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            if (action == null)
            {
                return;
            }

            var transaction = BeginTransaction();
            try
            {
                await action();

                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception e)
            {
                Logger.LogError(e.Message, e);

                await transaction.RollbackAsync(cancellationToken);

                throw;
            }
            finally
            {
                transaction.Dispose();
            }
        }


        public void Transaction(Action action)
        {
            if (action == null)
            {
                return;
            }

            var transaction = BeginTransaction();
            try
            {
                action();

                transaction.Commit();
            }
            catch (Exception e)
            {
                Logger.LogError(e.Message, e);

                transaction.Rollback();
            }
            finally
            {
                transaction.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!Disposed && Context != null && disposing)
            {
                Context.Dispose();
            }

            Disposed = true;
        }

    }
}
