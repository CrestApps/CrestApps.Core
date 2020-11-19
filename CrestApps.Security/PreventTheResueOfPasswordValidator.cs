using CrestApps.Data.Abstraction;
using CrestApps.Data.Entity;
using CrestApps.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace CrestApps.Security
{
    public class PreventTheResueOfPasswordValidator : IPasswordValidator<User>
    {
        private readonly IUnitOfWork UnitOfWork;
        private readonly PasswordHistoryValidationOptions Options;

        /// <summary>
        /// Total days to prevent the reuse
        /// </summary>
        public int TotalDays { get; set; }

        public PreventTheResueOfPasswordValidator(IUnitOfWork unitOfWork, IOptions<PasswordHistoryValidationOptions> options)
        {
            UnitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            Options = options != null ? options.Value : new PasswordHistoryValidationOptions();
        }

        public async Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user, string password)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            if (manager.PasswordHasher == null)
            {
                throw new ArgumentNullException(nameof(manager.PasswordHasher));
            }

            string passwordHash = manager.PasswordHasher.HashPassword(user, password);

            int days = Math.Abs(TotalDays);
            DateTime dateTime = DateTime.UtcNow.AddDays(days * -1);

            bool wasUsed = await UnitOfWork.UserHistoricalPasswords.Where(x => x.UserId == user.Id && x.PasswordHash == passwordHash && x.CreatedAt >= dateTime).HasAnyAsync();

            if (wasUsed)
            {
                var error = new IdentityError()
                {
                    Code = Options.ErrorCode,
                    Description = string.Format(Options.ErrorCodeTemplate, days),
                };

                return IdentityResult.Failed(error);
            }

            return IdentityResult.Success;
        }
    }
}
