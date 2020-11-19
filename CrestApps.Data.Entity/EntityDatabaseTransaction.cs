using CrestApps.Data.Core.Abstraction;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestApps.Data.Entity
{
    public sealed class EntityDatabaseTransaction : IDatabaseTransaction
    {
        private bool IsDisposed = false;

        private readonly IDbContextTransaction Transaction;

        public EntityDatabaseTransaction(DbContext context)
        {
            if (Transaction == null)
            {
                Transaction = context.Database.BeginTransaction();
            }
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            if (Transaction == null)
            {
                throw new InvalidOperationException("Something went wrong while trying to commit. The transaction was already commited, rolled back or disposed");
            }

            await Transaction.CommitAsync(cancellationToken);
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (Transaction == null)
            {
                throw new InvalidOperationException("Something went wrong while trying to rollback. The transaction was already rolledback, commited or disposed");
            }

            await Transaction.RollbackAsync(cancellationToken);
        }

        public void Commit()
        {
            if (Transaction == null)
            {
                throw new InvalidOperationException("Something went wrong while trying to commit. The transaction was already commited, rolled back or disposed");
            }

            Transaction.Commit();
        }

        public void Rollback()
        {
            if (Transaction == null)
            {
                throw new InvalidOperationException("Something went wrong while trying to rollback. The transaction was already rolledback, commited or disposed");
            }

            Transaction.Rollback();
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (IsDisposed)
            {
                return;
            }

            if (Transaction != null && disposing)
            {
                Transaction.Dispose();
            }

            IsDisposed = true;
        }
    }
}
