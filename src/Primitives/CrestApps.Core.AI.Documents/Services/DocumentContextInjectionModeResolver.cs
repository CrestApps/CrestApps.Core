using System.Text.RegularExpressions;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Documents.Services;

internal enum DocumentContextInjectionMode
{
    Search = 0,
    FullUserDocuments = 1,
}

internal static partial class DocumentContextInjectionModeResolver
{
    public static DocumentContextInjectionMode Resolve(OrchestrationContext context, int userDocumentCount)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (userDocumentCount <= 0 || string.IsNullOrWhiteSpace(context.UserMessage))
        {
            return DocumentContextInjectionMode.Search;
        }

        var message = context.UserMessage;

        if (ExplicitWholeDocumentPattern().IsMatch(message))
        {
            return DocumentContextInjectionMode.FullUserDocuments;
        }

        var hasWholeDocumentTask =
            WholeDocumentTaskPattern().IsMatch(message) ||
            WholeDocumentExtractionPattern().IsMatch(message) ||
            AboutDocumentPattern().IsMatch(message);

        if (!hasWholeDocumentTask)
        {
            return DocumentContextInjectionMode.Search;
        }

        if (userDocumentCount == 1)
        {
            return DocumentContextInjectionMode.FullUserDocuments;
        }

        if (DocumentReferencePattern().IsMatch(message))
        {
            return DocumentContextInjectionMode.FullUserDocuments;
        }

        return DocumentContextInjectionMode.Search;
    }

    public static IReadOnlyList<ChatDocumentInfo> ResolveUserSuppliedDocuments(PreemptiveRagContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Resource is ChatInteraction interaction &&
            interaction.Documents is { Count: > 0 })
        {
            return interaction.Documents;
        }

        if (context.Resource is AIProfile &&
            context.OrchestrationContext.CompletionContext?.AdditionalProperties is not null &&
            context.OrchestrationContext.CompletionContext.AdditionalProperties.TryGetValue("Session", out var sessionObject) &&
            sessionObject is AIChatSession session &&
            session.Documents is { Count: > 0 })
        {
            return session.Documents;
        }

        return [];
    }

    [GeneratedRegex(@"\b(full|entire|whole|complete)\s+(document|file|attachment|attachments|upload|uploads|uploaded file|uploaded files|pdf|text)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitWholeDocumentPattern();

    [GeneratedRegex(@"\b(summariz(?:e|es|ed|ing)|summaris(?:e|es|ed|ing)|summary|outline|overview|tldr|tl;dr|recap|abstract|review|critique|proofread|edit|rewrite|rephrase|improve|translate|walk me through|explain)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WholeDocumentTaskPattern();

    [GeneratedRegex(@"\b(list|extract|identify|find|pull|collect|enumerate|count)\b.{0,40}\b(all|every|complete|full)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex WholeDocumentExtractionPattern();

    [GeneratedRegex(@"\bwhat(?:'s| is)\s+(?:this|the|these|those)?\s*(document|documents|file|files|attachment|attachments|upload|uploads|pdf)\s+about\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AboutDocumentPattern();

    [GeneratedRegex(@"\b(document|documents|file|files|attachment|attachments|upload|uploads|uploaded file|uploaded files|pdf|pdfs)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DocumentReferencePattern();
}
