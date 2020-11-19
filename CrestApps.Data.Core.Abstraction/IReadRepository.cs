using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace CrestApps.Data.Core.Abstraction
{
    public interface IReadRepository<TModel, TKeyType>
       where TModel : class, IReadModel
       where TKeyType : struct
    {
        /// <summary>
        /// Get records by it's primary key asynchronous
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<TModel> GetAsync(TKeyType id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get records by it's primary key asynchronously
        /// </summary>
        /// <param name="id"></param>
        /// <exception cref="Core.Data.Exceptions.ModelNotFoundException">Throws exception when no matching model found</exception>
        /// <returns></returns>
        Task<TModel> FirstAsync(TKeyType id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all records from the storage asynchronously
        /// </summary>
        /// <returns></returns>
        Task<IEnumerable<TModel>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a condition exists in the storage asynchronously
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>
        Task<bool> AnyAsync(Expression<Func<TModel, bool>> predicate, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get the a single matching record or null asynchronously
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>
        Task<TModel> SingleOrDefaultAsync(Expression<Func<TModel, bool>> predicate, CancellationToken cancellationToken = default);


        /// <summary>
        /// Get records by it's primary key
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        TModel Get(TKeyType id);

        /// <summary>
        /// Get records by it's primary key
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>        TModel Get(TKeyType id);
        TModel First(TKeyType id);

        /// <summary>
        /// Gets all records from the storage
        /// </summary>
        /// <returns></returns>
        IEnumerable<TModel> GetAll();

        /// <summary>
        /// Checks if a condition exists in the storage
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>        
        bool Any(Expression<Func<TModel, bool>> predicate);

        /// <summary>
        /// Get the a single matching record or null
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>       
        TModel SingleOrDefault(Expression<Func<TModel, bool>> predicate);

        /// <summary>
        /// Get all records matching a lambda expression
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>
        IQueryable<TModel> Where(Expression<Func<TModel, bool>> predicate);

        /// <summary>
        /// Gets a queryable object with custom options
        /// </summary>
        /// <param name="options"></param>
        /// <returns></returns>
        IQueryable<TModel> Query(QueryOptions options = null);
    }


    public interface IReadRepository<TModel> : IReadRepository<TModel, int>
        where TModel : class, IReadModel
    {
    }
}

