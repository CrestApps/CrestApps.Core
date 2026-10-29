using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Speech;

/// <summary>
/// Opens the <see cref="IVoiceLibrary"/> of one speech provider. A provider that has a voice library registers one
/// next to its <c>IAIClientProvider</c>.
/// </summary>
public interface IVoiceLibraryProvider
{
    /// <summary>
    /// Determines whether this provider serves the named client (provider).
    /// </summary>
    /// <param name="clientName">The provider's client name.</param>
    bool CanHandle(string clientName);

    /// <summary>
    /// Opens the library for a connection.
    /// </summary>
    /// <param name="connection">The connection entry holding the provider's credentials.</param>
    IVoiceLibrary GetLibrary(AIProviderConnectionEntry connection);
}
