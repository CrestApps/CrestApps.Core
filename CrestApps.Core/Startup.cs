using CrestApps.Core.Service;
using CrestApps.Core.WebPresentation;
using CrestApps.Data.Abstraction;
using CrestApps.Data.Core.Abstraction;
using CrestApps.Data.Entity;
using CrestApps.Data.Models;
using CrestApps.Foundation;
using CrestApps.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Converters;
using OrchardCore.Environment.Shell;
using OrchardCore.Environment.Shell.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace CrestApps.Core
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages();

            services.AddControllersWithViews()
                    .AddRazorRuntimeCompilation()
                    .AddNewtonsoftJson(options =>
                    {
                        options.SerializerSettings.Converters.Add(new StringEnumConverter
                        {
                            AllowIntegerValues = false,
                        });
                    });

            services.AddSingleton<ILoggerFactory, LoggerFactory>();

            services.AddTransient<IDatabaseContextBuilderConfigurator, DefaultDatabaseContextBuilderConfigurator>();
            services.AddDbContext<ApplicationContext>(opts =>
            {
                opts.EnableSensitiveDataLogging();
            });

            services.AddIdentity<User, Role>()
                    .AddEntityFrameworkStores<ApplicationContext>()
                    .AddDefaultTokenProviders();

            services.AddOptions<ExtendedIdentityOptions>()
                    .Configure(options =>
                    {
                        options.SignIn.RequireConfirmedAccount = true;
                        options.Lockout.MaxFailedAccessAttempts = 5;

                        // lockout the user for 5 mins after 5 failed attempts
                        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);

                        options.Password.RequireDigit = true;
                        options.Password.RequireLowercase = true;
                        options.Password.RequireNonAlphanumeric = true;
                        options.Password.RequireUppercase = true;
                        options.Password.RequiredLength = 8;
                        options.SignIn.RequireConfirmedEmail = true;
                        options.EnableLockoutFailure = true;

                        // Lock out the user for 30 minutes after the first consecutive lockout
                        options.LockoutCounter.Add(1, TimeSpan.FromMinutes(30))

                                  // Lock out the user for 2 hours after the second consecutive lockout
                                  .Add(2, TimeSpan.FromHours(2))

                                  // Lock out the user for 1 day after the third consecutive lockout
                                  .Add(3, TimeSpan.FromDays(1))

                                  // Lock out the user for 7 days after the fourth consecutive lockout
                                  .Add(4, TimeSpan.FromDays(7));
                    });

            services.ConfigureApplicationCookie(options =>
            {
                options.LoginPath = $"/Account/Login";
                options.LogoutPath = $"/Account/Logout";
                options.AccessDeniedPath = $"/Account/AccessDenied";

                // Set the idle time. If the user is idle for 15 mins log them out.
                options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
                options.SlidingExpiration = true;
                options.Cookie.HttpOnly = true;
            });

            services.Configure<RouteOptions>(options =>
            {
                options.AppendTrailingSlash = true;
                options.LowercaseUrls = true;
            });

            // Register the fallback mailer
            services.Configure<EmailSenderOption>(Configuration.GetSection("Mail"));

            // Register the fallback database
            services.Configure<DbContentTenantOptions>(Configuration.GetSection("Database"));

            services.AddSingleton<IEmailSender, EmailSender>();

            services.Configure<RequestLocalizationOptions>(opts =>
                    {
                        var supportedCultures = new List<CultureInfo>
                        {
                            new CultureInfo("en-US"),
                        };

                        opts.DefaultRequestCulture = new RequestCulture("en-US");

                        // Formatting numbers, dates, etc.
                        opts.SupportedCultures = supportedCultures;
                        // UI strings that we have localized.
                        opts.SupportedUICultures = supportedCultures;
                    });

            services.AddLocalization(o =>
            {
                o.ResourcesPath = "Resources";
            });

            //services.AddMvc().AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix);

            services.AddOrchardCore()
                .AddMvc()
                .WithTenants()
                .ConfigureServices((tenantServices, serviceProvider) =>
                {
                    // tenantServices.AddLocalization(o =>
                    // {
                    //    o.ResourcesPath = "Resources";
                    // });

                    // Here we configure Tenant specific services and options
                    tenantServices.AddTypesUsingReflection();

                    tenantServices.AddSecureIdentity(new PasswordHistoryValidationOptions()
                    {
                        // prevent the use of password within 90 days
                        TotalDays = 90,
                    });
     
                    tenantServices.AddScoped<IUnitOfWork, UnitOfWork>();
                    tenantServices.AddScoped(typeof(IRepository<>), typeof(TenantEntityRepository<>));
                    tenantServices.AddScoped<IDateTimeConverter, DateTimeConverter>();
                    tenantServices.AddHttpContextAccessor()
                                  .AddScoped<IUserPassport, UserPassport>();

                    // get an instance of the configuration
                    IShellConfiguration tenantConfigs = serviceProvider.GetRequiredService<IShellConfiguration>();

                    var shellSettings = serviceProvider.GetService<ShellSettings>();

                    // Configure the mail provider
                    var tenantEmail = shellSettings.Mail();
                    if (tenantEmail != null && tenantEmail.HasOptions)
                    {
                        // If the tenant has it's own instance of the email, we register it.
                        tenantServices.AddEmailOptions(tenantEmail);
                    }

                    var tenantDatabase = shellSettings.Database();

                    if (tenantDatabase != null && tenantDatabase.HasProvider)
                    {
                        // If the tenant has it's own instance of a database, we'll use it
                        tenantServices.AddDatabaseOptions(tenantDatabase);
                    }
                });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                //app.UseDatabaseErrorPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
                app.UseHttpsRedirection();
            }
            app.UseStatusCodePagesWithReExecute("/error/{0}");
            app.UseStaticFiles();
            app.UseRouting();
            app.UseOrchardCore();

            // IStringLocalizer
            var options = app.ApplicationServices.GetService<IOptions<RequestLocalizationOptions>>();
            app.UseRequestLocalization(options.Value);

            app.UseCookiePolicy(new CookiePolicyOptions()
            {
                // To allow OAuth2 authentication change this to Lax
                // https://docs.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-3.1
                //MinimumSameSitePolicy = SameSiteMode.Strict
            });


            app.UseEndpoints(endpoints =>
            {
                /*
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
                */
                endpoints.MapRazorPages();
            });
        }
    }
}
