using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace CrestApps.Foundation
{
    public interface IUserPassport
    {
        Task<string> GetFirstNameAsync();
        Task<string> GetMiddleNameAsync();
        Task<string> GetLastNameAsync();
        Task<string> GetFullNameAsync();
        Task<string> GetUsernameAsync();

        IEnumerable<Claim> Claims { get; }
        bool IsAuthenticated { get; }
        Guid UserId { get; }

        string TimeZoneName { get; }
        Claim GetClaimByType(string claimType);
        Task<IUser> GetUserAsync();
        bool HasClaim(string claimType);
    }
}
