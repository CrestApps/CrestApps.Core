using System.Collections.Generic;
using System.Threading.Tasks;

namespace CrestApps.Data.Core.Abstraction
{
    public interface IDatabaseRawQuery
    {
        IEnumerable<T> SqlQuery<T>(string query, params object[] parameters) where T : class;

        IEnumerable<T> SqlQueryNoType<T>(string query, params object[] parameters);


        int? ExecuteSqlCommand(string query, params object[] parameters);

        Task<IEnumerable<T>> SqlQueryAsync<T>(string query, params object[] parameters);

        Task<int?> ExecuteSqlCommandAsync(string query, params object[] parameters);

    }
}

