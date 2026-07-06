using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;

namespace CrestApps.Core.AI.Documents.Generation;

/// <summary>
/// Resolves the conversation that a generated, downloadable document should be attached to. Generated
/// files are scoped to the active chat interaction or, for profile-backed conversations, the active chat
/// session so the download is authorized and resolvable through the normal document download path.
/// </summary>
public readonly record struct GeneratedDocumentScope
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeneratedDocumentScope"/> struct.
    /// </summary>
    /// <param name="referenceId">The owning conversation identifier.</param>
    /// <param name="referenceType">The owning conversation reference type.</param>
    public GeneratedDocumentScope(string referenceId, string referenceType)
    {
        ReferenceId = referenceId;
        ReferenceType = referenceType;
    }

    /// <summary>
    /// Gets the owning conversation identifier.
    /// </summary>
    public string ReferenceId { get; }

    /// <summary>
    /// Gets the owning conversation reference type.
    /// </summary>
    public string ReferenceType { get; }

    /// <summary>
    /// Resolves the generated-document scope from the current <see cref="AIInvocationScope"/>.
    /// </summary>
    /// <returns>The resolved scope, or <see langword="null"/> when no downloadable conversation scope is available.</returns>
    public static GeneratedDocumentScope? Resolve()
    {
        var invocationContext = AIInvocationScope.Current;
        var executionContext = invocationContext?.ToolExecutionContext;

        if (executionContext is null)
        {
            return null;
        }

        switch (executionContext.Resource)
        {
            case ChatInteraction interaction when !string.IsNullOrEmpty(interaction.ItemId):
                return new GeneratedDocumentScope(interaction.ItemId, AIReferenceTypes.Document.ChatInteraction);

            case AIProfile:
                var session = ResolveSession(invocationContext);

                return session is not null && !string.IsNullOrEmpty(session.SessionId)
                    ? new GeneratedDocumentScope(session.SessionId, AIReferenceTypes.Document.ChatSession)
                    : null;

            default:
                return null;
        }
    }

    private static AIChatSession ResolveSession(AIInvocationContext invocationContext)
    {
        if (invocationContext is null)
        {
            return null;
        }

        if (invocationContext.ChatSession is not null)
        {
            return invocationContext.ChatSession;
        }

        if (invocationContext.Items.TryGetValue(nameof(AIChatSession), out var value) && value is AIChatSession session)
        {
            return session;
        }

        return null;
    }
}
