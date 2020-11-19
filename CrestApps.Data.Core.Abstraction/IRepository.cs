using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CrestApps.Data.Core.Abstraction
{

    public interface IRepository<TModel, TKeyType> : IReadRepository<TModel, TKeyType>
        where TModel : class, IWriteModel
        where TKeyType : struct
    {
        // Add single record
        TModel Add(TModel entity);

        // Add multiple records
        IEnumerable<TModel> Add(IEnumerable<TModel> entities);

        // Remove records
        void Remove(TModel entity);

        // remove multiple records
        void Remove(IEnumerable<TModel> entities);

        void Update(TModel entity);
        void Update(IEnumerable<TModel> entries);

        void Save();
        Task SaveAsync(CancellationToken cancellation = default);
    }

    public interface IRepository<TModel> : IRepository<TModel, int>
        where TModel : class, IWriteModel
    {

    }
}
