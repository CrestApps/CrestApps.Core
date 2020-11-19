using CrestApps.Data.Core.Abstraction;
using CrestApps.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CrestApps.Data.Abstraction
{
    public interface IUnitOfWork : IUnitOfWorkBase
    {
        IRepository<User> Users { get; }
        IRepository<Role> Roles { get; }
        IRepository<UserClaim> UserClaims { get; }
        IRepository<UserRole> UserRoles { get; }
        IRepository<UserLogin> UserLogins { get; }
        IRepository<UserToken> UserTokens { get; }
        IRepository<UserHistoricalPassword> UserHistoricalPasswords { get; }
        IRepository<Log> Logs { get; }
    }
}
