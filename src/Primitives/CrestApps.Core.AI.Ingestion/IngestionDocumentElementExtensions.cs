using System.Text;
using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.AI.Ingestion;

/// <summary>
/// Reads the text worth embedding out of an ingestion element.
/// </summary>
/// <remarks>
/// The data ingestion library has its own equivalent, but both the class and the method are internal to that
/// package, so this reproduces its measured behaviour: a paragraph yields its text, an image yields its
/// alternative text or nothing at all, and a table yields its text.
/// </remarks>
public static class IngestionDocumentElementExtensions
{
    /// <summary>
    /// Gets the text that represents the element for embedding and prompting.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The text, or <see langword="null"/> when the element carries none.</returns>
    public static string GetSemanticText(this IngestionDocumentElement element)
    {
        if (element == null)
        {
            return null;
        }

        if (element is IngestionDocumentImage image)
        {
            if (!string.IsNullOrWhiteSpace(image.AlternativeText))
            {
                return image.AlternativeText;
            }

            return image.GetMetadataString(FigureMetadataKeys.Caption);
        }

        if (element is IngestionDocumentTable table)
        {
            if (!string.IsNullOrWhiteSpace(table.Text))
            {
                return table.Text;
            }

            return RenderCells(table);
        }

        return element.Text;
    }

    /// <summary>
    /// Gets the stable identifier assigned to a figure.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <returns>The figure identifier, or <see langword="null"/> when none was assigned.</returns>
    public static string GetFigureId(this IngestionDocumentImage image)
    {
        return image.GetMetadataString(FigureMetadataKeys.Id);
    }

    /// <summary>
    /// Gets the page the element was read from.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The one-based page number, or <see langword="null"/> when the element carries no page.</returns>
    public static int? GetPage(this IngestionDocumentElement element)
    {
        return element?.PageNumber;
    }

    /// <summary>
    /// Determines whether the element is page furniture: a running head, a footer or a printed page number
    /// that a reader marked with <see cref="ElementMetadataKeys.IsDecoration"/>.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns><see langword="true"/> when the element is decoration.</returns>
    /// <remarks>
    /// Decoration is kept in the document because structure analysis reads it: the running head says what
    /// kind of section a page belongs to and the folio says what page number is printed on it. It is never
    /// body text, so everything that embeds or displays text skips it through this one test.
    /// </remarks>
    public static bool IsDecoration(this IngestionDocumentElement element)
    {
        return element is not null &&
            element.HasMetadata &&
            element.Metadata.TryGetValue(ElementMetadataKeys.IsDecoration, out var value) &&
            value is true;
    }

    /// <summary>
    /// Reads one metadata value as a string.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="key">The metadata key.</param>
    /// <returns>The value, or <see langword="null"/> when the element carries no such entry.</returns>
    public static string GetMetadataString(this IngestionDocumentElement element, string key)
    {
        if (element == null || string.IsNullOrEmpty(key) || !element.HasMetadata)
        {
            return null;
        }

        if (!element.Metadata.TryGetValue(key, out var value) || value == null)
        {
            return null;
        }

        var text = value as string ?? value.ToString();

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string RenderCells(IngestionDocumentTable table)
    {
        var cells = table.Cells;

        if (cells == null || cells.Length == 0)
        {
            return null;
        }

        var builder = new StringBuilder();

        for (var row = 0; row < cells.GetLength(0); row++)
        {
            if (row > 0)
            {
                builder.Append('\n');
            }

            for (var column = 0; column < cells.GetLength(1); column++)
            {
                if (column > 0)
                {
                    builder.Append(" | ");
                }

                builder.Append(cells[row, column]?.Text);
            }
        }

        var rendered = builder.ToString();

        return string.IsNullOrWhiteSpace(rendered) ? null : rendered;
    }
}
