namespace CrestApps.Data.Core.Abstraction
{
    public interface IUnitOfWorkTransaction
    {
        IDatabaseTransaction BeginTransaction();
    }
}

