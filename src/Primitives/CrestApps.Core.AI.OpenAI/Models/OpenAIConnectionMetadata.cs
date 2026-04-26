namespace CrestApps.Core.AI.OpenAI.Models;

/// <summary>
/// Represents the open AI Connection Metadata.
/// </summary>
public sealed class OpenAIConnectionMetadata
{
    /// <summary>
    /// Gets or sets the endpoint.
    /// </summary>
    public Uri Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the api Key.
    /// </summary>
    public string ApiKey { get; set; }
}
