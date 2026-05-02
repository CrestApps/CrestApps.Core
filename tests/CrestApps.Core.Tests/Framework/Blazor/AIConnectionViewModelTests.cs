using CrestApps.Core.AI.Models;
using CrestApps.Core.Blazor.Web.ViewModels;

namespace CrestApps.Core.Tests.Framework.Blazor;

public sealed class AIConnectionViewModelTests
{
    [Fact]
    public void FromConnection_ShouldPreserveReadOnlyState()
    {
        var connection = new AIProviderConnection
        {
            ItemId = "config-openai",
            Name = "config-openai",
            DisplayText = "Config OpenAI",
            Source = "AzureOpenAI",
            IsReadOnly = true,
        };

        var model = AIConnectionViewModel.FromConnection(connection);

        Assert.True(model.IsReadOnly);
        Assert.Equal("Azure", model.Source);
    }
}
