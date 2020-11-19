namespace CrestApps.Data.Core.Abstraction
{
    public interface IDbConnection
    {
        /// <summary>
        /// Returns the connection name of the connection string
        /// </summary>
        string NameOrConnectionString { get; }

        string ConnectionString { get; }
    }

    public interface IDatabaseConnection : IDbConnection
    {
    }
}

