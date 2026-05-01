using CrestApps.Core.Startup.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CrestApps.Core.Tests.Framework.Startup;

public sealed class SharedWebApplicationBuilderExtensionsTests
{
    [Fact]
    public void AddSharedSampleHostDefaults_ShouldLoadProjectAndResolvedAppDataSettings()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "crestapps-tests", Guid.NewGuid().ToString("N"));
        var contentRootPath = Path.Combine(rootPath, "content-root");
        var projectAppDataPath = Path.Combine(contentRootPath, "App_Data");
        var resolvedAppDataPath = Path.Combine(rootPath, "resolved-app-data");

        Directory.CreateDirectory(projectAppDataPath);
        Directory.CreateDirectory(resolvedAppDataPath);

        try
        {
            File.WriteAllText(
                Path.Combine(projectAppDataPath, "appsettings.json"),
                """
                {
                  "CrestApps": {
                    "AI": {
                      "Deployments": [
                        {
                          "ClientName": "OpenAI",
                          "Name": "project-deployment",
                          "ModelName": "gpt-4.1",
                          "Type": "Chat"
                        }
                      ]
                    }
                  }
                }
                """);

            File.WriteAllText(
                Path.Combine(resolvedAppDataPath, "appsettings.json"),
                """
                {
                  "CrestApps": {
                    "AI": {
                      "Deployments": [
                        {
                          "Name": "resolved-deployment"
                        }
                      ]
                    }
                  }
                }
                """);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = contentRootPath,
                EnvironmentName = Environments.Development,
            });

            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string>
            {
                ["CrestApps:AppDataPath"] = resolvedAppDataPath,
            });

            var appDataPath = builder.AddSharedSampleHostDefaults();

            Assert.Equal(resolvedAppDataPath, appDataPath);
            Assert.Equal("OpenAI", builder.Configuration["CrestApps:AI:Deployments:0:ClientName"]);
            Assert.Equal("resolved-deployment", builder.Configuration["CrestApps:AI:Deployments:0:Name"]);
            Assert.Equal("gpt-4.1", builder.Configuration["CrestApps:AI:Deployments:0:ModelName"]);
            Assert.Equal("Chat", builder.Configuration["CrestApps:AI:Deployments:0:Type"]);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
