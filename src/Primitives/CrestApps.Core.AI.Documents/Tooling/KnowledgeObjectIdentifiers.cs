using CrestApps.Core.Infrastructure.Indexing;

namespace CrestApps.Core.AI.Documents.Tooling;

/// <summary>
/// What the knowledge tools accept as a knowledge object identifier.
/// </summary>
/// <remarks>
/// Every canonical identifier states its own kind before its first colon, which is the only thing about an
/// identifier that can be checked without going to the store. Both tools check it the same way, from one
/// list, so a kind added to <see cref="KnowledgeObjectTypes"/> cannot be readable by one tool and rejected
/// by the other.
/// </remarks>
internal static class KnowledgeObjectIdentifiers
{
    private static readonly string[] _prefixes =
    [
        KnowledgeObjectTypes.Document,
        KnowledgeObjectTypes.Article,
        KnowledgeObjectTypes.Text,
        KnowledgeObjectTypes.Figure,
        KnowledgeObjectTypes.Chart,
        KnowledgeObjectTypes.Table,
    ];

    /// <summary>
    /// Determines whether the supplied value is shaped like a knowledge object identifier.
    /// </summary>
    /// <param name="id">The value.</param>
    /// <returns><see langword="true"/> when it names a known kind.</returns>
    public static bool IsKnown(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        foreach (var prefix in _prefixes)
        {
            if (id.StartsWith(prefix + ':', StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
