using System.Text;
using Cysharp.Text;

namespace CrestApps.Core.AI.Documents.Generation;

/// <summary>
/// Writes <see cref="GeneratedFileContent"/> as UTF-8 encoded text. This writer backs the plain-text
/// document formats (for example <c>.txt</c>, <c>.md</c>, <c>.json</c>, <c>.html</c>, and <c>.yaml</c>);
/// the body text is written verbatim so the caller controls the exact textual representation. When only
/// tabular data is supplied the rows are rendered as a Markdown-style table.
/// </summary>
public sealed class PlainTextGeneratedFileWriter : IGeneratedFileWriter
{
    /// <summary>
    /// Writes the content as UTF-8 text to the destination stream.
    /// </summary>
    /// <param name="content">The content to write.</param>
    /// <param name="destination">The destination stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task WriteAsync(GeneratedFileContent content, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(destination);

        var text = !string.IsNullOrEmpty(content.Text)
            ? content.Text
            : RenderTable(content);

        await using var writer = new StreamWriter(
            destination,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            leaveOpen: true);

        if (!string.IsNullOrEmpty(content.Title) && string.IsNullOrEmpty(content.Text))
        {
            await writer.WriteAsync(content.Title.AsMemory(), cancellationToken);
            await writer.WriteAsync(Environment.NewLine.AsMemory(), cancellationToken);
            await writer.WriteAsync(Environment.NewLine.AsMemory(), cancellationToken);
        }

        await writer.WriteAsync(text.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private static string RenderTable(GeneratedFileContent content)
    {
        if (!content.HasTable)
        {
            return string.Empty;
        }

        using var builder = ZString.CreateStringBuilder();
        builder.Append("| ");
        builder.Append(string.Join(" | ", content.Header));
        builder.Append(" |");
        builder.Append(Environment.NewLine);
        builder.Append("| ");
        builder.Append(string.Join(" | ", content.Header.Select(_ => "---")));
        builder.Append(" |");
        builder.Append(Environment.NewLine);

        if (content.Rows is not null)
        {
            foreach (var row in content.Rows)
            {
                builder.Append("| ");
                builder.Append(string.Join(" | ", row.Select(value => (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal))));
                builder.Append(" |");
                builder.Append(Environment.NewLine);
            }
        }

        return builder.ToString();
    }
}
