namespace CrestApps.Core.AI.Chat.Privacy;

/// <summary>
/// A personal detail found in a conversation, to be replaced wherever it appears.
/// </summary>
public sealed class AIPersonalDataTerm
{
    /// <summary>
    /// Gets or sets the exact text.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the kind: <c>name</c>, <c>address</c>, <c>birthdate</c> or <c>id</c>.
    /// </summary>
    public string Kind { get; set; }
}
