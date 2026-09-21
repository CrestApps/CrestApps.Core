using Cysharp.Text;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Writes a <see cref="TabularPreviewGrid"/> out as a Markdown table, for when the host cannot show a
/// picture.
/// </summary>
/// <remarks>
/// Markdown rather than hand-written HTML: every chat surface renders an assistant message through the same
/// markdown parser and then wraps each <c>&lt;table&gt;</c> it produced in the site's own table styling. A
/// table written as markdown therefore arrives looking like the rest of the page, whereas raw markup has to
/// survive the sanitizer and then misses that styling.
/// </remarks>
internal static class TabularPreviewTableRenderer
{
    /// <summary>
    /// Renders the grid.
    /// </summary>
    /// <param name="grid">The shaped grid.</param>
    /// <returns>The markdown table, with its heading and caption.</returns>
    public static string Render(TabularPreviewGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        using var builder = ZString.CreateStringBuilder();

        if (!string.IsNullOrWhiteSpace(grid.Title))
        {
            builder.Append("**");
            builder.Append(grid.Title);
            builder.AppendLine("**");
            builder.AppendLine();
        }

        builder.Append("| ");
        builder.Append(string.Join(" | ", grid.Columns.Select(column => Escape(column.Header))));
        builder.AppendLine(" |");

        // The alignment row carries the same left/right split the drawn preview uses, so numbers line up on
        // their last digit in either rendering.
        builder.Append("| ");
        builder.Append(string.Join(" | ", grid.Columns.Select(column => column.IsNumeric ? "---:" : "---")));
        builder.AppendLine(" |");

        foreach (var row in grid.Rows)
        {
            builder.Append("| ");
            builder.Append(string.Join(" | ", row.Select(Escape)));
            builder.AppendLine(" |");
        }

        builder.AppendLine();
        builder.Append(grid.BuildCaption(grid.Columns.Count));

        if (!string.IsNullOrWhiteSpace(grid.Subtitle))
        {
            builder.Append(" — ");
            builder.Append(grid.Subtitle);
        }

        builder.Append('.');

        return builder.ToString();
    }

    /// <summary>
    /// Makes a value safe to sit inside a table cell.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The escaped value, or a non-breaking placeholder when it is empty.</returns>
    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return " ";
        }

        // A pipe ends the cell, so an unescaped one in the data silently shifts every value after it into
        // the wrong column.
        return value.Contains('|', StringComparison.Ordinal) ? value.Replace("|", "\\|", StringComparison.Ordinal) : value;
    }
}
