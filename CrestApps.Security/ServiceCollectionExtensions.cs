using CrestApps.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Security
{
    public static class ServiceCollectionExtensions
    {

        public static IServiceCollection AddSecureIdentity(this IServiceCollection service)
        {
            return service.AddIdentityStore()
                          .AddIdentityManager()
                          .AddIdentityPreventResueOfPasswords();
        }

        public static IServiceCollection AddSecureIdentity(this IServiceCollection service, PasswordHistoryValidationOptions options)
        {
            service.AddSecureIdentity();

            if (options != null)
            {
                service.AddIdentityPreventResueOfPasswordsOptions(options);
            }

            return service;
        }

        public static IServiceCollection AddIdentityStore(this IServiceCollection service)
        {

            service.AddScoped<IUserStore<User>, IdentityUserStore>();

            return service;
        }

        public static IServiceCollection AddIdentityManager(this IServiceCollection service)
        {
            service.AddScoped<UserManager<User>, IdentityUserManager>();

            return service;
        }

        public static IServiceCollection AddIdentityPreventResueOfPasswords(this IServiceCollection service)
        {
            service.AddScoped<IPasswordValidator<User>, PreventTheResueOfPasswordValidator>();

            return service;
        }

        public static IServiceCollection AddIdentityPreventResueOfPasswordsOptions(this IServiceCollection service, PasswordHistoryValidationOptions options)
        {
            service.AddOptions<PasswordHistoryValidationOptions>()
                    .Configure(opts =>
                    {
                        opts.TotalDays = options.TotalDays;
                        opts.ErrorCode = options.ErrorCode;
                        opts.ErrorCodeTemplate = options.ErrorCodeTemplate;
                    });


            return service;
        }
    }
}
