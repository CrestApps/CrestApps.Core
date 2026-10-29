#nullable enable
using CrestApps.Core.AI.Cartesia;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Connections;
using CrestApps.Core.AI.ElevenLabs;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Speech;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrestApps.Core.Tests.Core.Speech;

public sealed class VoiceLibraryRegistrationTests
{
    [Fact]
    public void BothProviders_ResolveFromAScopeWithScopeValidationOn()
    {
        var services = new ServiceCollection();
        services.AddCoreAIElevenLabs();
        services.AddCoreAICartesia();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        var clients = scope.ServiceProvider.GetServices<IAIClientProvider>().ToList();
        var libraries = scope.ServiceProvider.GetServices<IVoiceLibraryProvider>().ToList();

        Assert.Contains(clients, c => c.CanHandle(ElevenLabsConstants.ClientName));
        Assert.Contains(clients, c => c.CanHandle(CartesiaConstants.ClientName));
        Assert.Equal(2, libraries.Count);
    }

    [Fact]
    public async Task TheResolver_OpensTheLibraryOfTheDeploymentsProvider_AndReturnsNullForOthers()
    {
        var library = Mock.Of<IVoiceLibrary>();
        var libraryProvider = new Mock<IVoiceLibraryProvider>();
        libraryProvider.Setup(p => p.CanHandle("ElevenLabs")).Returns(true);
        libraryProvider.Setup(p => p.GetLibrary(It.IsAny<AIProviderConnectionEntry>())).Returns(library);

        var connections = new Mock<IAIProviderConnectionStore>();
        var resolver = new DefaultVoiceLibraryResolver(
            [libraryProvider.Object],
            [],
            Mock.Of<IDataProtectionProvider>(),
            connections.Object);

        // A deployment with a stored connection that no longer exists says so instead of speaking with the wrong key.
        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.GetLibraryAsync(
            new AIDeployment { ClientName = "ElevenLabs", ConnectionName = "gone" },
            TestContext.Current.CancellationToken));

        Assert.Null(await resolver.GetLibraryAsync(new AIDeployment { ClientName = "OpenAI", ConnectionName = "any" }, TestContext.Current.CancellationToken));
    }
}
