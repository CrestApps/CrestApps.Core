using Microsoft.AspNetCore.Identity;

namespace CrestApps.Security
{
    public class ExtendedIdentityOptions : IdentityOptions
    {
        //
        // Summary:
        //     Gets or sets the Microsoft.AspNetCore.Identity.LockoutOptions for the identity
        //     system.
        //
        // Value:
        //     The Microsoft.AspNetCore.Identity.LockoutOptions for the identity system.
        public LockoutCounterOptions LockoutCounter { get; set; }

        public bool EnableLockoutFailure { get; set; }

        public ExtendedIdentityOptions()
            : base()
        {
            LockoutCounter = new LockoutCounterOptions();
        }

    }
}
