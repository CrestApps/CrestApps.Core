using CrestApps.Core.Service;
using CrestApps.Data.Entity;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core
{
    public static class ServiceCollectionTenantExtensions
    {

        public static IServiceCollection AddEmailOptions(this IServiceCollection service, EmailSenderOption options)
        {
            service.Configure<EmailSenderOption>(c =>
            {
                c.Host = options.Host;
                c.Port = options.Port;
                c.SenderUsername = options.SenderUsername;
                c.SenderPassword = options.SenderPassword;
                c.SenderEmail = options.SenderEmail;
                c.SenderName = options.SenderName;
                c.UseSSL = options.UseSSL;
            });

            return service;
        }


        public static IServiceCollection AddDatabaseOptions(this IServiceCollection service, DbContentTenantOptions options)
        {
            service.Configure<DbContentTenantOptions>(c =>
            {
                c.ApplicationName = options.ApplicationName;
                c.ConnectionString = options.ConnectionString;
                c.Password = options.Password;
                c.Username = options.Username;
                c.Version = options.Version;
            });

            return service;
        }
    }
}
