namespace CrestApps.Data.Entity
{
    public class DbContentTenantOptions
    {
        // supported providers can be found https://docs.microsoft.com/en-us/ef/core/providers/?tabs=dotnet-core-cli

        public DatabaseProvider? Provider { get; set; }

        public string ConnectionString { get; set; }

        public string ApplicationName { get; set; }

        public string Username { get; set; }

        public string Password { get; set; }

        // Here we can add more options for different providers

        public bool HasProvider => Provider.HasValue;

        public string Version { get; set; }

    }
}
