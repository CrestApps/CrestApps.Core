using System.Text.Json;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Editing;

/// <summary>
/// Reads the theme and page setup a model writes as JSON, and applies a page setup to a section.
/// </summary>
internal static class WordSetupReader
{
    /// <summary>
    /// Reads a theme onto a design: the preset first, when given, then every override.
    /// </summary>
    /// <param name="theme">The theme object.</param>
    /// <param name="baseDesign">The design the theme changes, or <see langword="null"/> to start from the preset.</param>
    /// <returns>The design.</returns>
    public static WordDesign ReadTheme(JsonElement theme, WordDesign baseDesign)
    {
        var preset = WordJsonValues.GetString(theme, "preset");
        var design = !string.IsNullOrWhiteSpace(preset) ? WordDesign.FromPreset(preset) : baseDesign?.Clone() ?? new WordDesign();

        if (theme.ValueKind != JsonValueKind.Object)
        {
            return design;
        }

        design.BodyFont = WordJsonValues.GetString(theme, "body_font") ?? design.BodyFont;
        design.HeadingFont = WordJsonValues.GetString(theme, "heading_font") ?? design.HeadingFont;
        design.BodySize = Math.Clamp(WordJsonValues.GetDouble(theme, "body_size") ?? design.BodySize, 6, 32);
        design.TextColor = Color(theme, "text_color") ?? design.TextColor;
        design.HeadingColor = Color(theme, "heading_color") ?? design.HeadingColor;
        design.AccentColor = Color(theme, "accent_color") ?? design.AccentColor;
        design.LinkColor = Color(theme, "link_color") ?? design.LinkColor;
        design.LineSpacing = Math.Clamp(WordJsonValues.GetDouble(theme, "line_spacing") ?? design.LineSpacing, 0.8, 3);
        design.ParagraphSpacing = Math.Clamp(WordJsonValues.GetDouble(theme, "paragraph_spacing") ?? design.ParagraphSpacing, 0, 48);
        design.TableHeaderFill = Color(theme, "table_header_fill") ?? (Color(theme, "accent_color") ?? design.TableHeaderFill);
        design.TableHeaderTextColor = Color(theme, "table_header_text_color") ?? (WordColor.IsDark(design.TableHeaderFill) ? "FFFFFF" : "222222");
        design.TableBandFill = WordJsonValues.TryGet(theme, "table_band_fill", out _)
            ? Color(theme, "table_band_fill")
            : Color(theme, "accent_color") is { } accent ? WordColor.Tint(accent, 0.9) : design.TableBandFill;
        design.TableBorderColor = Color(theme, "table_border_color") ?? design.TableBorderColor;

        return design;
    }

    /// <summary>
    /// Applies a page setup to a section: <c>size</c>, <c>orientation</c>, <c>margins</c>, <c>columns</c> and
    /// <c>column_spacing</c>. Anything not given is kept.
    /// </summary>
    /// <param name="section">The section properties.</param>
    /// <param name="setup">The page setup object.</param>
    /// <returns>What changed, for the tool's answer.</returns>
    public static List<string> ApplyPageSetup(SectionProperties section, JsonElement setup)
    {
        ArgumentNullException.ThrowIfNull(section);

        var changes = new List<string>();

        if (setup.ValueKind != JsonValueKind.Object)
        {
            return changes;
        }

        var (currentWidth, currentHeight) = WordSections.PageSize(section);
        var sizeName = WordJsonValues.GetString(setup, "size") ?? WordJsonValues.GetString(setup, "paper_size");
        var orientation = WordJsonValues.GetString(setup, "orientation");
        var width = currentWidth;
        var height = currentHeight;

        if (!string.IsNullOrWhiteSpace(sizeName))
        {
            if (!WordPageSizes.TryGet(sizeName, out width, out height))
            {
                throw new WordToolException($"Unknown page size \"{sizeName}\". Use one of: {string.Join(", ", WordPageSizes.Names)}.");
            }

            changes.Add("page size " + sizeName);
        }

        var landscape = orientation is null
            ? currentWidth > currentHeight
            : string.Equals(orientation.Trim(), "landscape", StringComparison.OrdinalIgnoreCase);

        if (orientation is not null)
        {
            changes.Add(landscape ? "landscape" : "portrait");
        }

        if (sizeName is not null || orientation is not null)
        {
            WordPageSizes.SetPageSize(section, width, height, landscape);
        }

        if (WordJsonValues.TryGet(setup, "margins", out var margins))
        {
            var current = WordSections.Margins(section);
            var top = current.Top;
            var right = current.Right;
            var bottom = current.Bottom;
            var left = current.Left;
            var header = current.Header;
            var footer = current.Footer;

            if (margins.ValueKind == JsonValueKind.Object)
            {
                top = WordFormatReader.ReadLength(margins, "top") ?? top;
                right = WordFormatReader.ReadLength(margins, "right") ?? right;
                bottom = WordFormatReader.ReadLength(margins, "bottom") ?? bottom;
                left = WordFormatReader.ReadLength(margins, "left") ?? left;
                header = WordFormatReader.ReadLength(margins, "header") ?? header;
                footer = WordFormatReader.ReadLength(margins, "footer") ?? footer;
            }
            else if (ReadLength(margins) is { } all)
            {
                top = right = bottom = left = all;
            }
            else
            {
                throw new WordToolException("'margins' must be a length (1in, 2.5cm, 72) or { top, right, bottom, left, header, footer }.");
            }

            WordPageSizes.SetMargins(section, Margin(top), Margin(right), Margin(bottom), Margin(left), Margin(header), Margin(footer));
            changes.Add(FormattableString.Invariant($"margins {top / 72:0.##}/{right / 72:0.##}/{bottom / 72:0.##}/{left / 72:0.##} in"));
        }

        var columns = WordJsonValues.GetInt(setup, "columns");

        if (columns is not null)
        {
            var count = Math.Clamp(columns.Value, 1, 6);
            var spacing = WordFormatReader.ReadLength(setup, "column_spacing") ?? 36;

            WordSchemaOrder.Set(section, new Columns
            {
                ColumnCount = (Int16Value)(short)count,
                Space = WordUnits.ToTwips(spacing).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Separator = WordJsonValues.GetBoolean(setup, "column_separator") == true ? true : null,
            });

            changes.Add(count == 1 ? "one column" : $"{count} columns");
        }

        return changes;
    }

    private static string Color(JsonElement theme, string name)
    {
        var value = WordJsonValues.GetString(theme, name);

        if (value is null)
        {
            return null;
        }

        return WordColor.TryParse(value, out var color)
            ? color
            : throw new WordToolException($"\"{value}\" is not a color for '{name}'. Use hex such as #1F4E79, rgb(31, 78, 121), or a color name.");
    }

    private static double? ReadLength(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && WordUnits.TryParseLength(value.GetString(), 612, out var points) ? points : null;
    }

    private static double Margin(double points)
    {
        return Math.Clamp(points, 0, 288);
    }
}
