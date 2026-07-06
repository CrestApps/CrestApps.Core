using System.Text;

namespace CrestApps.Core.AI.Documents.Generation;

/// <summary>
/// Writes <see cref="GeneratedFileContent"/> as an RFC 4180 comma-separated values (CSV) file.
/// When the content carries a tabular header the values are written as structured rows; otherwise the
/// free-form text is written verbatim so that already-delimited content is preserved as-is.
/// </summary>
public sealed class DelimitedGeneratedFileWriter : IGeneratedFileWriter
{
    /// <summary>
    /// Writes the content as CSV to the destination stream.
    /// </summary>
    /// <param name="content">The content to write.</param>
    /// <param name="destination">The destination stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task WriteAsync(GeneratedFileContent content, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(destination);

        await using var writer = new StreamWriter(
            destination,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            leaveOpen: true);

        if (content.HasTable)
        {
            await WriteRowAsync(writer, content.Header, cancellationToken);

            if (content.Rows is not null)
            {
                foreach (var row in content.Rows)
                {
                    await WriteRowAsync(writer, row, cancellationToken);
                }
            }
        }
        else if (!string.IsNullOrEmpty(content.Text))
        {
            await writer.WriteAsync(content.Text.AsMemory(), cancellationToken);
        }

        await writer.FlushAsync(cancellationToken);
    }

    private static async Task WriteRowAsync(StreamWriter writer, IReadOnlyList<string> values, CancellationToken cancellationToken)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                await writer.WriteAsync(",".AsMemory(), cancellationToken);
            }

            await writer.WriteAsync(EscapeCsvValue(values[i]).AsMemory(), cancellationToken);
        }

        await writer.WriteAsync(Environment.NewLine.AsMemory(), cancellationToken);
    }

    private static string EscapeCsvValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (!value.Contains('"', StringComparison.Ordinal) &&
            !value.Contains(',', StringComparison.Ordinal) &&
            !value.Contains('\n', StringComparison.Ordinal) &&
            !value.Contains('\r', StringComparison.Ordinal))
        {
            return value;
        }

        return string.Concat("\"", value.Replace("\"", "\"\"", StringComparison.Ordinal), "\"");
    }
}
