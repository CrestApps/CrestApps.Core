using System.Text;

namespace CrestApps.Core.AI.Ingestion.Pdf.Services;

/// <summary>
/// One table found on a page.
/// </summary>
internal sealed class DetectedTable
{
    /// <summary>
    /// Gets or sets the cell text, row by row, top to bottom and left to right.
    /// </summary>
    public string[][] Cells { get; set; } = [];

    /// <summary>
    /// Gets or sets the region the table occupies, as left, bottom, right and top in PDF user space.
    /// </summary>
    public double[] Bounds { get; set; } = [];

    /// <summary>
    /// Gets how many rows the table has.
    /// </summary>
    public int RowCount => Cells.Length;

    /// <summary>
    /// Gets how many columns the table has.
    /// </summary>
    public int ColumnCount => Cells.Length == 0 ? 0 : Cells[0].Length;

    /// <summary>
    /// Renders the table as markdown, which is what an ingestion table carries as its text.
    /// </summary>
    /// <returns>The markdown.</returns>
    public string ToMarkdown()
    {
        if (RowCount == 0 || ColumnCount == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        for (var row = 0; row < RowCount; row++)
        {
            builder.Append("| ");
            builder.AppendJoin(" | ", Cells[row].Select(cell => cell.Replace("|", "\\|", StringComparison.Ordinal)));
            builder.AppendLine(" |");

            if (row == 0)
            {
                builder.Append('|');

                for (var column = 0; column < ColumnCount; column++)
                {
                    builder.Append(" --- |");
                }

                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd();
    }
}
