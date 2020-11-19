using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestApps.Data.Core.Abstraction
{
    public interface IDatabaseTransaction : IDisposable
    {
        void Commit();

        void Rollback();

        Task CommitAsync(CancellationToken cancellationToken = default);
        Task RollbackAsync(CancellationToken cancellationToken = default);
    }
}
