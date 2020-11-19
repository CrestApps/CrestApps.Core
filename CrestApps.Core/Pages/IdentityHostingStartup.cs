using Microsoft.AspNetCore.Hosting;

[assembly: HostingStartup(typeof(CrestApps.Core.Areas.Identity.Pages.IdentityHostingStartup))]
namespace CrestApps.Core.Areas.Identity.Pages
{
    public class IdentityHostingStartup : IHostingStartup
    {
        public void Configure(IWebHostBuilder builder)
        {
            builder.ConfigureServices((context, services) =>
            {
            });
        }
    }
}