using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using System;
using System.Configuration;

namespace CrestApps.Data.Entity
{
    public class DefaultDatabaseContextBuilderConfigurator : IDatabaseContextBuilderConfigurator
    {
        private readonly DbContentTenantOptions Config;

        public DefaultDatabaseContextBuilderConfigurator(IOptions<DbContentTenantOptions> options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            Config = options.Value;
        }

        public void Configure(DbContextOptionsBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (Config.Provider == DatabaseProvider.SqlServer)
            {
                ConfigureSqlServer(builder);
            }
            else if (Config.Provider == DatabaseProvider.MySQL)
            {
                ConfigureMySqlServer(builder);
            }
            else if (Config.Provider == DatabaseProvider.MariaDb)
            {
                ConfigureMariaDbServer(builder);
            }
            else
            {
                throw new ArgumentOutOfRangeException($"The given provider is not supported.");
            }
        }

        protected void ConfigureSqlServer(DbContextOptionsBuilder builder)
        {
            if (string.IsNullOrWhiteSpace(Config.ConnectionString))
            {
                throw new ConfigurationErrorsException($"{nameof(Config.ConnectionString)} is require when using configuring SqlServer.");
            }

            builder.UseSqlServer(Config.ConnectionString);
        }

        protected void ConfigureMySqlServer(DbContextOptionsBuilder builder)
        {
            ValidateForMySql();

            builder.UseMySql(Config.ConnectionString, new MySqlServerVersion(Config.Version));
        }

        protected void ConfigureMariaDbServer(DbContextOptionsBuilder builder)
        {
            ValidateForMySql();

            builder.UseMySql(Config.ConnectionString, new MariaDbServerVersion(Config.Version));
        }

        private void ValidateForMySql()
        {
            if (string.IsNullOrWhiteSpace(Config.ConnectionString))
            {
                throw new ConfigurationErrorsException($"{nameof(Config.ConnectionString)} is require when using configuring MySQL server.");
            }

            if (string.IsNullOrWhiteSpace(Config.Version))
            {
                throw new ConfigurationErrorsException($"{nameof(Config.Version)} is require when using configuring MySQL server.");
            }
        }
    }
}
