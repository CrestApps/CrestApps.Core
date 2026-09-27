using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// The sentences the document-property tools share, so every tool reports a saved copy, a quoted value or
/// a size the same way.
/// </summary>
internal static class PdfPropertiesToolText
{
    /// <summary>
    /// Says where an edit was saved.
    /// </summary>
    /// <param name="target">The PDF that was edited.</param>
    /// <param name="working">The working PDF the result was saved as.</param>
    /// <returns>The sentence.</returns>
    public static string Saved(PdfSource target, PdfWorkingDocument working)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(working);

        return target.IsUpload
            ? $"Saved as working PDF \"{working.Name}\" ({working.PageCount} pages). The upload \"{target.Name}\" was not changed."
            : $"Saved working PDF \"{working.Name}\" ({working.PageCount} pages).";
    }

    /// <summary>
    /// Starts a sentence with a capital letter.
    /// </summary>
    /// <param name="text">The sentence.</param>
    /// <returns>The sentence, capitalized.</returns>
    public static string Capitalize(string text)
    {
        return string.IsNullOrEmpty(text)
            ? text
            : char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>
    /// Quotes a value for an answer, cutting a long one.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="limit">The most characters shown.</param>
    /// <returns>The quoted value, or <c>(none)</c>.</returns>
    public static string Quote(string value, int limit = 200)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "(none)";
        }

        var text = value.Replace('\n', ' ').Replace('\r', ' ');

        return text.Length > limit
            ? "\"" + text[..limit] + "…\""
            : "\"" + text + "\"";
    }

    /// <summary>
    /// Writes a size in bytes for an answer.
    /// </summary>
    /// <param name="bytes">The size.</param>
    /// <returns>For example <c>1,234,567 bytes (1.2 MB)</c>.</returns>
    public static string Size(long bytes)
    {
        var readable = bytes switch
        {
            >= 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $" ({bytes / (1024d * 1024):0.0} MB)"),
            >= 1024 => string.Create(CultureInfo.InvariantCulture, $" ({bytes / 1024d:0.0} KB)"),
            _ => string.Empty,
        };

        return string.Create(CultureInfo.InvariantCulture, $"{bytes:N0} bytes{readable}");
    }

    /// <summary>
    /// Writes a PDF date for an answer.
    /// </summary>
    /// <param name="pdfDate">The PDF date.</param>
    /// <returns>For example <c>2026-09-27T10:32:48-07:00</c>, the raw value when it cannot be read, or <c>(none)</c>.</returns>
    public static string Date(string pdfDate)
    {
        if (string.IsNullOrWhiteSpace(pdfDate))
        {
            return "(none)";
        }

        return PdfXmpPacket.ToXmpDate(pdfDate) ?? pdfDate;
    }

    /// <summary>
    /// Adds the lines that are not empty to an answer.
    /// </summary>
    /// <param name="builder">The answer.</param>
    /// <param name="lines">The lines.</param>
    public static void AppendNotes(StringBuilder builder, params string[] lines)
    {
        ArgumentNullException.ThrowIfNull(builder);

        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                builder.AppendLine(line);
            }
        }
    }
}
