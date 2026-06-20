using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI;

/// <summary>
/// Represents a registered AI completion client entry.
/// </summary>
public sealed class AICompletionClientEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AICompletionClientEntry"/> class.
    /// </summary>
    /// <param name="clientName">The client name.</param>
    public AICompletionClientEntry(string clientName)
    {
        ClientName = clientName;
    }

    /// <summary>
    /// Gets the client name.
    /// </summary>
    public string ClientName { get; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public LocalizedString DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public LocalizedString Description { get; set; }
}
