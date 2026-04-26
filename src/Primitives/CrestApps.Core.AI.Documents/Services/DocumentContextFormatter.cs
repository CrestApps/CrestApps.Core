using CrestApps.Core.AI.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Services;

internal static class DocumentContextFormatter
{
    /// <summary>
    /// Format document text from chunkss document text from chunks.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="document">The document.</param>
    /// <param name="maxLength">The max length.</param>
    public static async Task<string> FormatDocumentTextFromChunksAsync(IServiceProvider services, AIDocument document, int? maxLength = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(document);

        var chunkStore = services.GetService<IAIDocumentChunkStore>();

        if (chunkStore is null)
        {
            return $"Document '{document.FileName}' has no extractable text content.";
        }

        var chunks = await chunkStore.GetChunksByAIDocumentIdAsync(document.ItemId);

        if (chunks.Count == 0)
        {
            return $"Document '{document.FileName}' has no extractable text content.";
        }

        var text = string.Join(Environment.NewLine, chunks.OrderBy(c => c.Index).Select(c => c.Content));

return FormatDocumentText(document.FileName, text, maxLength);
    }

    /// <summary>
    /// Formats document text.
    /// </summary>
    /// <param name="fileName">The file name.</param>
    /// <param name="text">The text.</param>
    /// <param name="maxLength">The max length.</param>
    public static string FormatDocumentText(string fileName, string text, int? maxLength = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return $"Document '{fileName}' has no extractable text content.";
        }

        if (maxLength is > 0 && text.Length > maxLength.Value)
        {
            text = string.Concat(text.AsSpan(0, maxLength.Value), "\n\n... [content truncated]");
        }

        return $"[Document: {fileName}]\n\n{text}";
    }
}
