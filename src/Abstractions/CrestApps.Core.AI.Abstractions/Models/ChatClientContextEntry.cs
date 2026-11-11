namespace CrestApps.Core.AI.Models;

/// <summary>
/// One piece of context a chat client sends with a message, such as the page the user is on or the record they are
/// editing. It reaches the model as data, never as instructions.
/// </summary>
public sealed class ChatClientContextEntry
{
    /// <summary>
    /// Gets or sets what the value describes, for example "Current page".
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the value, as text.
    /// </summary>
    public string Value { get; set; }
}
