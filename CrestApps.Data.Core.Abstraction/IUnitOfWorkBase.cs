using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestApps.Data.Core.Abstraction
{
    public interface IUnitOfWorkBase
    {
        int Save();

        Task<int> SaveAsync(CancellationToken cancellationToken = default);

        IDatabaseTransaction BeginTransaction();

        Task TransactionAsync(Func<Task> action, CancellationToken cancellationToken = default);

        void Transaction(Action action);
    }
}
