using CrestApps.Common.Helpers;
using CrestApps.Data.Entity;
using OrchardCore.Environment.Shell;

namespace CrestApps.Core.Service
{
    public static class ShellSettingsExtensions
    {
        public static string GetTenantId(this ShellSettings shellSettings)
        {
            return shellSettings["Identifier"] ?? shellSettings.Name;
        }

        public static bool IsEqualTo(this ShellSettings shellSettings, string tenantId)
        {
            return shellSettings.GetTenantId() == tenantId;
        }


        public static DbContentTenantOptions Database(this ShellSettings shellSettings)
        {
            var options = new DbContentTenantOptions
            {
                Provider = EnumHelper.ValueOrNull<DatabaseProvider>(shellSettings["Database_Provider"]),
                ConnectionString = shellSettings["Database_ConnectionString"],
                ApplicationName = shellSettings["Database_ApplicationName"],
                Username = shellSettings["Database_Username"],
                Password = shellSettings["Database_Password"],
            };

            return options;
        }

        public static EmailSenderOption Mail(this ShellSettings shellSettings)
        {
            var options = new EmailSenderOption()
            {
                Host = shellSettings["Mail_Host"],
                SenderEmail = shellSettings["Mail_SenderEmail"],
                SenderName = shellSettings["Mail_SenderName"],
                SenderUsername = shellSettings["Mail_SenderUsername"],
                SenderPassword = shellSettings["Mail_SenderPassword"],
            };

            if (int.TryParse(shellSettings["Mail_Port"], out int port))
            {
                options.Port = port;
            }

            if (bool.TryParse(shellSettings["Mail_UseSSL"], out bool useSSL))
            {
                options.UseSSL = useSSL;
            }

            return options;
        }
    }
}
