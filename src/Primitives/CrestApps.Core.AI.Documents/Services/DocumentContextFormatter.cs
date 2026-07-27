using System.Text;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Services;

internal static class DocumentContextFormatter
{
    /// <summary>
/// Formats document text from stored chunks.
    /// </summary>
/// <param name="services">The service provider.</param>
    /// <param name="document">The document.</param>
/// <param name="maxLength">The maximum content length.</param>
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

        var orderedChunks = chunks.OrderBy(chunk => chunk.Index).ToArray();

        if (!HasExtractableContent(orderedChunks))
        {
            return $"Document '{document.FileName}' has no extractable text content.";
        }

        if (maxLength is not > 0)
        {
            var text = string.Join(Environment.NewLine, orderedChunks.Select(chunk => chunk.Content));

            return FormatDocumentText(document.FileName, text, maxLength);
        }

        var joinedLength = GetJoinedLength(orderedChunks);

        if (joinedLength <= maxLength.Value)
        {
            var text = string.Join(Environment.NewLine, orderedChunks.Select(chunk => chunk.Content));

            return FormatDocumentText(document.FileName, text, maxLength);
        }

        return FormatTruncatedDocumentText(document.FileName, orderedChunks, maxLength.Value);
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

    private static bool HasExtractableContent(AIDocumentChunk[] chunks)
    {
        foreach (var chunk in chunks)
        {
            if (!string.IsNullOrWhiteSpace(chunk.Content))
            {
                return true;
            }
        }

        return false;
    }

    private static long GetJoinedLength(AIDocumentChunk[] chunks)
    {
        var length = (long)Environment.NewLine.Length * (chunks.Length - 1);

        foreach (var chunk in chunks)
        {
            length += chunk.Content?.Length ?? 0;
        }

        return length;
    }

    private static string FormatTruncatedDocumentText(
        string fileName,
        AIDocumentChunk[] chunks,
        int maxLength)
    {
        const string truncatedSuffix = "\n\n... [content truncated]";
        var documentPrefix = $"[Document: {fileName}]\n\n";
        var builder = new StringBuilder(documentPrefix.Length + maxLength + truncatedSuffix.Length);
        builder.Append(documentPrefix);
        var remainingLength = maxLength;

        for (var i = 0; i < chunks.Length && remainingLength > 0; i++)
        {
            if (i > 0)
            {
                AppendTruncated(builder, Environment.NewLine, ref remainingLength);
            }

            AppendTruncated(builder, chunks[i].Content, ref remainingLength);
        }

        builder.Append(truncatedSuffix);

        return builder.ToString();
    }

    private static void AppendTruncated(
        StringBuilder builder,
        string value,
        ref int remainingLength)
    {
        if (string.IsNullOrEmpty(value) || remainingLength == 0)
        {
            return;
        }

        var length = Math.Min(value.Length, remainingLength);
        builder.Append(value.AsSpan(0, length));
        remainingLength -= length;
    }
}
