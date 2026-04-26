namespace CrestApps.Core.AI.OpenAI.Azure.Models;

/// <summary>
/// Represents the azure Client Options.
/// </summary>
public sealed class AzureClientOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether enables Logging.
    /// </summary>
    public bool EnableLogging { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether enables Message Content Logging.
    /// </summary>
    public bool EnableMessageContentLogging { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether enables Message Logging.
    /// </summary>
    public bool EnableMessageLogging { get; set; }
}
