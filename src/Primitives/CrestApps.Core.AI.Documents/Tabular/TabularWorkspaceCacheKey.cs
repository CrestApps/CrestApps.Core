namespace CrestApps.Core.AI.Documents.Tabular;

internal sealed class TabularWorkspaceCacheKey
{
    public TabularWorkspaceCacheKey(
        string value,
        string chatInteractionId,
        string chatSessionId,
        string profileId,
        IReadOnlyList<(string ReferenceType, string ReferenceId)> references)
    {
        Value = value;
        ChatInteractionId = chatInteractionId;
        ChatSessionId = chatSessionId;
        ProfileId = profileId;
        References = references ?? [];
    }

    public string Value { get; }

    public string ChatInteractionId { get; }

    public string ChatSessionId { get; }

    public string ProfileId { get; }

    public IReadOnlyList<(string ReferenceType, string ReferenceId)> References { get; }
}
