using CrestApps.Common.Helpers;
using CrestApps.Data.Models;
using CrestApps.Foundation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace CrestApps.Core.WebPresentation
{
    public class UserPassport : ClaimsPrincipal, IUserPassport
    {
        private readonly UserManager<User> UserManager;
        private IUser User;

        // IHttpContextAccessor context
        public UserPassport(UserManager<User> userManager, IHttpContextAccessor contextAccessor)
        {
            UserManager = userManager ?? throw new ArgumentNullException(nameof(userManager));

            if (contextAccessor == null)
            {
                throw new ArgumentNullException(nameof(contextAccessor));
            }

            if (contextAccessor.HttpContext != null && contextAccessor.HttpContext.User != null)
            {
                var identity = new ClaimsIdentity(contextAccessor.HttpContext.User.Claims, contextAccessor.HttpContext.User.Identity?.AuthenticationType);

                AddIdentity(identity);
            }
        }


        public string UserId
        {
            get
            {
                string userId = UserManager.GetUserId(this);

                if (string.IsNullOrWhiteSpace(userId))
                {
                    throw new UnauthorizedAccessException("User not logged in!");
                }


                return userId;
            }
        }



        public async Task<string> GetFirstNameAsync()
        {
            var user = await GetUserAsync();

            return user.FirstName;
        }

        public async Task<string> GetMiddleNameAsync()
        {
            var user = await GetUserAsync();

            return user.MiddleName;
        }

        public async Task<string> GetLastNameAsync()
        {
            var user = await GetUserAsync();

            return user.LastName;
        }

        public async Task<string> GetFullNameAsync()
        {
            IUser user = await GetUserAsync();

            return Str.Merge(user.FirstName, user.MiddleName, user.LastName);
        }
        public async Task<string> GetUsernameAsync()
        {
            IUser user = await GetUserAsync();

            return await UserManager.GetUserNameAsync(user as User);
        }

        public bool IsAuthenticated => Identity != null && Identity.IsAuthenticated;

        public string TimeZoneName => GetUserAsync().Result.TimeZoneName;

        public Claim GetClaimByType(string claimType)
        {
            return Claims.FirstOrDefault(c => c.Type == claimType);
        }

        public bool HasClaim(string claimType)
        {
            return Claims.Any(c => c.Type == claimType);
        }


        public async Task<IUser> GetUserAsync()
        {
            if (User == null)
            {
                User = await UserManager.GetUserAsync(this);
            }

            return User;
        }
    }
}
