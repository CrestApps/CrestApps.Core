using CrestApps.Core.AI.Models;
using CrestApps.Core.Blazor.Web.ViewModels;

namespace CrestApps.Core.Tests.Framework.Blazor;

public sealed class AIDeploymentViewModelTests
{
    [Fact]
    public void FromDeployment_ShouldPreserveReadOnlyState()
    {
        var deployment = new AIDeployment
        {
            ItemId = "config-gpt-4.1",
            Name = "gpt-4.1",
            ModelName = "gpt-4.1",
            ClientName = "AzureOpenAI",
            Type = AIDeploymentType.Chat,
            IsReadOnly = true,
        };

        var model = AIDeploymentViewModel.FromDeployment(deployment);

        Assert.True(model.IsReadOnly);
        Assert.Equal("Azure", model.ClientName);
    }
}
