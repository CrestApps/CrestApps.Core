using CrestApps.Data.Abstraction;
using CrestApps.Data.Core.Abstraction;
using CrestApps.Data.Models;
using Microsoft.Extensions.Logging;
using OrchardCore.Environment.Shell;

namespace CrestApps.Data.Entity
{
    public class UnitOfWork : EntityUnitOfWorkBase<ApplicationContext, UnitOfWork>, IUnitOfWork
    {
        public IRepository<User> Users { get; private set; }
        public IRepository<Role> Roles { get; private set; }
        public IRepository<UserClaim> UserClaims { get; private set; }
        public IRepository<UserRole> UserRoles { get; private set; }
        public IRepository<UserLogin> UserLogins { get; private set; }
        public IRepository<UserToken> UserTokens { get; private set; }

        public IRepository<UserHistoricalPassword> UserHistoricalPasswords { get; private set; }

        public IRepository<Log> Logs { get; private set; }



        public UnitOfWork(ApplicationContext context, ShellSettings shellSettings, ILogger<UnitOfWork> logger)
            : base(context, logger)
        {
            Users = new TenantEntityRepository<User>(context, shellSettings);
            Roles = new TenantEntityRepository<Role>(context, shellSettings);
            UserClaims = new TenantEntityRepository<UserClaim>(context, shellSettings);
            UserRoles = new TenantEntityRepository<UserRole>(context, shellSettings);
            UserLogins = new TenantEntityRepository<UserLogin>(context, shellSettings);
            UserTokens = new TenantEntityRepository<UserToken>(context, shellSettings);
            UserHistoricalPasswords = new TenantEntityRepository<UserHistoricalPassword>(context, shellSettings);
            Logs = new TenantEntityRepository<Log>(context, shellSettings);
        }

    }
}
