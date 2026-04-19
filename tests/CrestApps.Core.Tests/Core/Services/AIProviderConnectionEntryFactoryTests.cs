using System.Text.Json;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Azure.Models;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class AIProviderConnectionEntryFactoryTests
{
    [Fact]
    public void Create_AppliesInitializingHandlersToStoredConnection()
    {
        var connection = new AIProviderConnection
        {
            Name = "azure-connection",
            DisplayText = "Azure Connection",
            ClientName = "azure-openai",
            Properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(AzureConnectionMetadata)] = JsonSerializer.SerializeToNode(new AzureConnectionMetadata
                {
                    Endpoint = new Uri("https://example.openai.azure.com/"),
                    AuthenticationType = AzureAuthenticationType.ApiKey,
                    ApiKey = "test-key",
                    IdentityId = "managed-identity",
                }),
                ["Region"] = "eastus",
            },
        };

        var entry = AIProviderConnectionEntryFactory.Create(connection, [new TestAzureConnectionHandler()]);

        Assert.Equal("Azure Connection", entry["DisplayText"]);
        Assert.Equal("https://example.openai.azure.com/", entry["Endpoint"]);
        Assert.Equal("ApiKey", entry["AuthenticationType"]);
        Assert.Equal("test-key", entry["ApiKey"]);
        Assert.Equal("managed-identity", entry["IdentityId"]);
        Assert.Equal("eastus", entry["Region"]);
    }

    private sealed class TestAzureConnectionHandler : IAIProviderConnectionHandler
    {
        public void Exporting(ExportingAIProviderConnectionContext context)
        {
        }

        public void Initializing(InitializingAIProviderConnectionContext context)
        {
            var metadata = context.Connection.GetOrCreate<AzureConnectionMetadata>();

            context.Values["Endpoint"] = metadata.Endpoint?.ToString();
            context.Values["AuthenticationType"] = metadata.AuthenticationType.ToString();
            context.Values["ApiKey"] = metadata.ApiKey;
            context.Values["IdentityId"] = metadata.IdentityId;
        }
    }
}
