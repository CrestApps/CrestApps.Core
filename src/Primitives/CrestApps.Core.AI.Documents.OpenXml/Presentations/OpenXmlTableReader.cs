using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;
using DocumentFormat.OpenXml;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Reads a DrawingML table.
/// </summary>
/// <remarks>
/// A cell with its own fill and borders is read exactly. A cell that leaves them to a built-in table style is
/// given the look of PowerPoint's default style in the style's accent colour: the style definitions ship with
/// PowerPoint, not with the file, so the file alone cannot say more. Tables this agent writes always carry
/// their own formatting, so for them the preview is exact.
/// </remarks>
internal static class OpenXmlTableReader
{
    private const string NoStyleNoGrid = "{2D5ABB26-0587-4C30-8999-92F81FD0307C}";
    private const string NoStyleTableGrid = "{5940675A-B579-460E-94D1-54222C63F5DA}";

    private static readonly Dictionary<string, string> _mediumStyleAccents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["{073A0DAA-6AF3-43AB-8588-CEC1D06C72B9}"] = "dk1",
        ["{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}"] = "accent1",
        ["{21E4AEA4-8DFA-4A89-87EB-49C32662AFE8}"] = "accent2",
        ["{F5AB1C69-6EDB-4FF4-983F-18BD219EF322}"] = "accent3",
        ["{00A15C55-8517-42AA-B614-E9B94910E393}"] = "accent4",
        ["{7DF18680-E054-41AD-8BC1-D1AEF772440D}"] = "accent5",
        ["{93296810-A885-4BE3-A3E7-6D5BEEA58F35}"] = "accent6",
    };

    /// <summary>
    /// Reads a table.
    /// </summary>
    /// <param name="table">The <c>a:tbl</c> element.</param>
    /// <param name="context">The slide context.</param>
    /// <param name="directory">The deck's slides, for links in cell text.</param>
    /// <param name="options">Whether pictures are loaded.</param>
    /// <returns>The table.</returns>
    public static PresentationTable Read(OpenXmlElement table, OpenXmlSlideContext context, OpenXmlSlideDirectory directory, PresentationReadOptions options)
    {
        var properties = OpenXmlMarkup.Child(table, "tblPr");
        var result = new PresentationTable
        {
            FirstRow = OpenXmlMarkup.Bool(properties, "firstRow") == true,
            BandedRows = OpenXmlMarkup.Bool(properties, "bandRow") == true,
            FirstColumn = OpenXmlMarkup.Bool(properties, "firstCol") == true,
            LastRow = OpenXmlMarkup.Bool(properties, "lastRow") == true,
            StyleId = OpenXmlMarkup.Child(properties, "tableStyleId")?.InnerText,
        };

        foreach (var column in OpenXmlMarkup.Children(OpenXmlMarkup.Child(table, "tblGrid"), "gridCol"))
        {
            result.ColumnWidths.Add(OpenXmlMarkup.Long(column, "w") ?? 0);
        }

        var rows = OpenXmlMarkup.Children(table, "tr").ToList();
        var style = ResolveStyle(result.StyleId, context);

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            result.RowHeights.Add(OpenXmlMarkup.Long(row, "h") ?? 370_840);

            var cells = new List<PresentationTableCell>();
            var columnIndex = 0;

            foreach (var cellElement in OpenXmlMarkup.Children(row, "tc"))
            {
                var look = style.Look(result, rowIndex, columnIndex, rows.Count);
                cells.Add(ReadCell(cellElement, context, directory, options, look));
                columnIndex++;
            }

            result.Rows.Add(cells);
        }

        return result;
    }

    private static PresentationTableCell ReadCell(
        OpenXmlElement cellElement,
        OpenXmlSlideContext context,
        OpenXmlSlideDirectory directory,
        PresentationReadOptions options,
        CellLook look)
    {
        var properties = OpenXmlMarkup.Child(cellElement, "tcPr");
        var textBody = OpenXmlMarkup.Child(cellElement, "txBody");
        var listStyles = new List<OpenXmlElement> { OpenXmlMarkup.Child(textBody, "lstStyle"), context.DefaultTextStyle };
        var defaults = new OpenXmlTextDefaults
        {
            Font = context.Theme.MinorFont,
            Color = look.TextColor,
            Bold = look.Bold,
            Size = 18,
        };

        var text = OpenXmlTextReader.Read(textBody, context, listStyles, [], defaults, directory);

        // The cell's margins and anchor live on the cell properties rather than on a body properties
        // element, so they replace what the text body read.
        text.InsetLeft = OpenXmlMarkup.Long(properties, "marL") ?? 91_440;
        text.InsetRight = OpenXmlMarkup.Long(properties, "marR") ?? 91_440;
        text.InsetTop = OpenXmlMarkup.Long(properties, "marT") ?? 45_720;
        text.InsetBottom = OpenXmlMarkup.Long(properties, "marB") ?? 45_720;
        text.VerticalAnchor = OpenXmlMarkup.Attribute(properties, "anchor") switch
        {
            "ctr" => "middle",
            "b" => "bottom",
            _ => "top",
        };

        var cell = new PresentationTableCell
        {
            Text = text,
            ColumnSpan = (int)Math.Clamp(OpenXmlMarkup.Long(cellElement, "gridSpan") ?? 1, 1, 1000),
            RowSpan = (int)Math.Clamp(OpenXmlMarkup.Long(cellElement, "rowSpan") ?? 1, 1, 1000),
            IsMerged = OpenXmlMarkup.Bool(cellElement, "hMerge") == true || OpenXmlMarkup.Bool(cellElement, "vMerge") == true,
        };

        cell.Fill = OpenXmlDrawingReader.ReadFill(OpenXmlDrawingReader.FindFill(properties), context, context.OwnerPart, options)
            ?? (look.Fill is null ? PresentationFill.None : PresentationFill.Solid(look.Fill));

        cell.BorderLeft = OpenXmlDrawingReader.ReadLine(OpenXmlMarkup.Child(properties, "lnL"), context) ?? look.Border;
        cell.BorderRight = OpenXmlDrawingReader.ReadLine(OpenXmlMarkup.Child(properties, "lnR"), context) ?? look.Border;
        cell.BorderTop = OpenXmlDrawingReader.ReadLine(OpenXmlMarkup.Child(properties, "lnT"), context) ?? look.Border;
        cell.BorderBottom = OpenXmlDrawingReader.ReadLine(OpenXmlMarkup.Child(properties, "lnB"), context) ?? look.Border;

        return cell;
    }

    private static TableStyleLook ResolveStyle(string styleId, OpenXmlSlideContext context)
    {
        if (string.IsNullOrEmpty(styleId) || string.Equals(styleId, NoStyleNoGrid, StringComparison.OrdinalIgnoreCase))
        {
            return new TableStyleLook(null, null, context.Colors.ResolveScheme("tx1") ?? "000000", null);
        }

        if (string.Equals(styleId, NoStyleTableGrid, StringComparison.OrdinalIgnoreCase))
        {
            var text = context.Colors.ResolveScheme("tx1") ?? "000000";

            return new TableStyleLook(null, null, text, new PresentationLine { Color = text, Width = 1 });
        }

        var accent = _mediumStyleAccents.TryGetValue(styleId, out var slot) ? slot : "accent1";
        var accentColor = context.Colors.ResolveScheme(accent) ?? "4472C4";
        var light = context.Colors.ResolveScheme("lt1") ?? "FFFFFF";

        return new TableStyleLook(
            accentColor,
            light,
            context.Colors.ResolveScheme("dk1") ?? "000000",
            new PresentationLine { Color = light, Width = 1 });
    }

    /// <summary>
    /// The look a built-in style gives a cell that sets nothing itself.
    /// </summary>
    private readonly record struct CellLook(string Fill, string TextColor, bool Bold, PresentationLine Border);

    /// <summary>
    /// A built-in table style, approximated by PowerPoint's default style in one colour.
    /// </summary>
    private sealed record TableStyleLook(string Accent, string HeaderText, string BodyText, PresentationLine Border)
    {
        public CellLook Look(PresentationTable table, int row, int column, int rowCount)
        {
            if (Accent is null)
            {
                return new CellLook(null, BodyText, false, Border);
            }

            var isHeader = table.FirstRow && row == 0;
            var isTotal = table.LastRow && row == rowCount - 1 && rowCount > 1;

            if (isHeader || isTotal)
            {
                return new CellLook(Accent, HeaderText, true, Border);
            }

            if (table.FirstColumn && column == 0)
            {
                return new CellLook(Accent, HeaderText, true, Border);
            }

            var bodyRow = table.FirstRow ? row - 1 : row;
            var tint = table.BandedRows && bodyRow % 2 == 0 ? 0.4 : 0.2;

            return new CellLook(PresentationColor.Tint(Accent, tint), BodyText, false, Border);
        }
    }
}
