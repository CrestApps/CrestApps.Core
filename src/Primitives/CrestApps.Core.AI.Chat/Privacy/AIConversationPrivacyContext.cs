using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Chat.Privacy;

/// <summary>
/// A conversation a storage mode is being applied to, passed to each <see cref="IAIConversationPrivacyHandler"/>.
/// </summary>
public sealed class AIConversationPrivacyContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIConversationPrivacyContext"/> class.
    /// </summary>
    /// <param name="session">The chat session.</param>
    /// <param name="mode">The storage mode being applied.</param>
    public AIConversationPrivacyContext(AIChatSession session, AIDataStorageMode mode)
    {
        ArgumentNullException.ThrowIfNull(session);

        Session = session;
        Mode = mode;
    }

    /// <summary>
    /// Gets the chat session.
    /// </summary>
    public AIChatSession Session { get; }

    /// <summary>
    /// Gets the storage mode being applied: <see cref="AIDataStorageMode.RedactPersonalData"/> or
    /// <see cref="AIDataStorageMode.MetadataOnly"/>.
    /// </summary>
    public AIDataStorageMode Mode { get; }

    /// <summary>
    /// Gets the texts handlers keep about the conversation, such as summaries and evaluation notes, added while
    /// collecting so the personal details in them are found together with those in the transcript.
    /// </summary>
    public IList<string> Texts { get; } = [];

    /// <summary>
    /// Gets the personal details found in the conversation, set before handlers apply a
    /// <see cref="AIDataStorageMode.RedactPersonalData"/> mode. Empty for <see cref="AIDataStorageMode.MetadataOnly"/>.
    /// </summary>
    public IReadOnlyList<AIPersonalDataTerm> Terms { get; internal set; } = [];

    /// <summary>
    /// Gets the names that are not personal data and must stay in the conversation: the assistant's own name and the
    /// business's. Seeded from <see cref="AIProfileDataPrivacySettings.NamesToKeep"/>; handlers may add the names
    /// they know while collecting.
    /// </summary>
    public IList<string> NamesToKeep { get; } = [];

    /// <summary>
    /// Gets a place for handlers to keep what they loaded while collecting, to change it while applying.
    /// </summary>
    public IDictionary<string, object> Items { get; } = new Dictionary<string, object>(StringComparer.Ordinal);

    /// <summary>
    /// Replaces the personal details in a text: those recognized by pattern and the <see cref="Terms"/> found.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The redacted text.</returns>
    public string Redact(string text)
    {
        return AIPersonalDataRedactor.Redact(text, Terms);
    }

    /// <summary>
    /// Replaces the personal details in consecutive turns of one speaker, finding them in the turns joined together so
    /// a detail spoken over two turns is replaced in both.
    /// </summary>
    /// <param name="turns">The turns of one speaker, in order.</param>
    /// <returns>The redacted turns, one for each turn.</returns>
    public IReadOnlyList<string> RedactTurns(IReadOnlyList<string> turns)
    {
        return AIPersonalDataRedactor.RedactTurns(turns, Terms);
    }
}
