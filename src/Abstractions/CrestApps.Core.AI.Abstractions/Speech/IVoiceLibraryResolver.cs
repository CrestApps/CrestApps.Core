using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Speech;

/// <summary>
/// Opens the voice library of a deployment's provider.
/// </summary>
public interface IVoiceLibraryResolver
{
    /// <summary>
    /// Gets the voice library of the deployment's provider, or <see langword="null"/> when the provider has none.
    /// </summary>
    /// <param name="deployment">The deployment containing the provider and connection.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IVoiceLibrary> GetLibraryAsync(AIDeployment deployment, CancellationToken cancellationToken = default);
}
