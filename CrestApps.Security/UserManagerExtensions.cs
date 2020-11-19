using CrestApps.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace CrestApps.Security
{
    public static class UserManagerExtensions
    {
        /// <summary>
        /// Change the security stamp for the account that matches the given email
        /// </summary>
        /// <param name="userManager"></param>
        /// <param name="email">the email address that belongs to the user to change the security stamp for</param>
        /// <param name="logger">If the logger is null, nothing will be logged</param>
        /// <returns></returns>
        public static async Task UpdateSecurityStampAsync(this UserManager<User> userManager, string email, ILogger logger = null)
        {
            var user = await userManager.FindByEmailAsync(email);

            if (user != null)
            {
                logger?.LogWarning("Updating the security stamp");

                await userManager.UpdateSecurityStampAsync(user);

                return;
            }
        }
    }
}
