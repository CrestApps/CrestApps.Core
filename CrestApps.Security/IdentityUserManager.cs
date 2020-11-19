using CrestApps.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CrestApps.Security
{
    public class IdentityUserManager : UserManager<User>
    {
        protected LockoutCounterOptions LockoutCounterOptions { get; private set; }

        public IdentityUserManager(IUserStore<User> store, IOptions<ExtendedIdentityOptions> optionsAccessor,
            IPasswordHasher<User> passwordHasher,
            IEnumerable<IUserValidator<User>> userValidators,
            IEnumerable<IPasswordValidator<User>> passwordValidators,
            ILookupNormalizer keyNormalizer,
            IdentityErrorDescriber errors,
            IServiceProvider services, ILogger<UserManager<User>> logger)
            : base(store, optionsAccessor, passwordHasher, userValidators, passwordValidators, keyNormalizer, errors, services, logger)
        {
            LockoutCounterOptions = (optionsAccessor.Value?.LockoutCounter) ?? new LockoutCounterOptions();
        }

        /// <summary>
        /// Increments the access failed count for the user as an asynchronous operation.
        /// If the failed access account is greater than or equal to the configured maximum number of attempts,
        /// the user will be locked out for the configured lockout time span.
        /// </summary>
        /// <param name="user">The user whose failed access count to increment.</param>
        /// <returns>The <see cref="Task"/> that represents the asynchronous operation, containing the <see cref="IdentityResult"/> of the operation.</returns>
        public override async Task<IdentityResult> AccessFailedAsync(User user)
        {
            ThrowIfDisposed();
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            var store = GetUserLockoutStore();
            var storeCounter = GetUserLockoutCounterStore();
            // If this puts the user over the threshold for lockout, lock them out and reset the access failed count
            var count = await store.IncrementAccessFailedCountAsync(user, CancellationToken);
            if (count < Options.Lockout.MaxFailedAccessAttempts)
            {
                return await UpdateUserAsync(user);
            }
            Logger.LogWarning(12, "User is locked out.");
            await store.SetLockoutEndDateAsync(user, GetNewLockOut(user), CancellationToken);
            // Increment the counter
            await storeCounter.IncrementLockoutCountAsync(user, CancellationToken);

            // At this point we know that the user is locked out, lets increment the lockout counter
            await store.ResetAccessFailedCountAsync(user, CancellationToken);
            return await UpdateUserAsync(user);
        }


        private DateTimeOffset GetNewLockOut(User user)
        {
            if (LockoutCounterOptions.EnableLockoutCounter && user.LockoutCount > 0)
            {
                // At this point we know that the user was locked-out at least once
                // let's see if there is a lockout-Count time
                var findTimeSpan = LockoutCounterOptions.GetBest(user.LockoutCount);

                if (findTimeSpan.HasValue)
                {
                    // Generate lockout time using the lockoutCount
                    return DateTimeOffset.UtcNow.Add(findTimeSpan.Value);
                }
            }

            // Generate lockout time from the default
            return DateTimeOffset.UtcNow.Add(Options.Lockout.DefaultLockoutTimeSpan);
        }

        /// <summary>
        /// Resets the access failed count for the specified <paramref name="user"/>.
        /// </summary>
        /// <param name="user">The user whose failed access count should be reset.</param>
        /// <returns>The <see cref="Task"/> that represents the asynchronous operation, containing the <see cref="IdentityResult"/> of the operation.</returns>
        public override async Task<IdentityResult> ResetAccessFailedCountAsync(User user)
        {
            ThrowIfDisposed();
            var store = GetUserLockoutStore();
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            var storeLockout = GetUserLockoutCounterStore();
            await storeLockout.ResetLockoutCountAsync(user, CancellationToken);
            if (await GetAccessFailedCountAsync(user) == 0)
            {
                return IdentityResult.Success;
            }
            await store.ResetAccessFailedCountAsync(user, CancellationToken);
            return await UpdateUserAsync(user);
        }

        private IUserLockoutStore<User> GetUserLockoutStore()
        {
            var cast = Store as IUserLockoutStore<User>;
            if (cast == null)
            {
                throw new NotSupportedException("StoreNotIUserLockoutStore");
            }
            return cast;
        }

        private IUserLockoutCounterStore<User> GetUserLockoutCounterStore()
        {
            var cast = Store as IUserLockoutCounterStore<User>;
            if (cast == null)
            {
                throw new NotSupportedException("StoreNotIUserLockoutStore");
            }
            return cast;
        }
    }
}
