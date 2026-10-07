using System.Text.Json;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Workspace;

namespace CrestApps.Core.AI.Documents.Word.Editing;

/// <summary>
/// Reads the formatting a model writes as JSON into run and paragraph formats.
/// </summary>
internal static class WordFormatReader
{
    /// <summary>
    /// Reads a run format: <c>font</c>, <c>size</c>, <c>bold</c>, <c>italic</c>, <c>underline</c>,
    /// <c>strikethrough</c>, <c>color</c>, <c>highlight</c>, <c>shading</c>, <c>superscript</c>, <c>subscript</c>,
    /// <c>all_caps</c>, <c>small_caps</c>, <c>character_spacing</c>, <c>language</c>.
    /// </summary>
    /// <param name="element">The JSON object.</param>
    /// <returns>The format, or <see langword="null"/> when the element is not an object.</returns>
    public static WordRunFormat ReadRun(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var underline = WordJsonValues.TryGet(element, "underline", out var underlineValue)
            ? underlineValue.ValueKind switch
            {
                JsonValueKind.True => "single",
                JsonValueKind.False => "none",
                JsonValueKind.String => underlineValue.GetString(),
                _ => null,
            }
            : null;

        return new WordRunFormat
        {
            Font = WordJsonValues.GetString(element, "font") ?? WordJsonValues.GetString(element, "font_family"),
            Size = WordJsonValues.GetDouble(element, "size") ?? WordJsonValues.GetDouble(element, "font_size"),
            Bold = WordJsonValues.GetBoolean(element, "bold"),
            Italic = WordJsonValues.GetBoolean(element, "italic"),
            Underline = underline,
            Strikethrough = WordJsonValues.GetBoolean(element, "strikethrough") ?? WordJsonValues.GetBoolean(element, "strike"),
            Color = WordJsonValues.GetString(element, "color"),
            Highlight = WordJsonValues.GetString(element, "highlight"),
            Shading = WordJsonValues.GetString(element, "shading") ?? WordJsonValues.GetString(element, "background"),
            Superscript = WordJsonValues.GetBoolean(element, "superscript"),
            Subscript = WordJsonValues.GetBoolean(element, "subscript"),
            AllCaps = WordJsonValues.GetBoolean(element, "all_caps"),
            SmallCaps = WordJsonValues.GetBoolean(element, "small_caps"),
            CharacterSpacing = WordJsonValues.GetDouble(element, "character_spacing"),
            Language = WordJsonValues.GetString(element, "language"),
        };
    }

    /// <summary>
    /// Reads a paragraph format.
    /// </summary>
    /// <param name="element">The JSON object.</param>
    /// <returns>The format, or <see langword="null"/> when the element is not an object.</returns>
    public static WordParagraphFormat ReadParagraph(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new WordParagraphFormat
        {
            Alignment = WordJsonValues.GetString(element, "alignment"),
            SpaceBefore = WordJsonValues.GetDouble(element, "space_before"),
            SpaceAfter = WordJsonValues.GetDouble(element, "space_after"),
            LineSpacing = WordJsonValues.GetDouble(element, "line_spacing"),
            ExactLineHeight = WordJsonValues.GetDouble(element, "line_height"),
            IndentLeft = ReadLength(element, "indent_left"),
            IndentRight = ReadLength(element, "indent_right"),
            FirstLineIndent = ReadLength(element, "first_line_indent"),
            HangingIndent = ReadLength(element, "hanging_indent"),
            KeepWithNext = WordJsonValues.GetBoolean(element, "keep_with_next"),
            KeepLinesTogether = WordJsonValues.GetBoolean(element, "keep_lines_together"),
            PageBreakBefore = WordJsonValues.GetBoolean(element, "page_break_before"),
            Shading = WordJsonValues.GetString(element, "shading") ?? WordJsonValues.GetString(element, "background"),
            Border = WordJsonValues.GetString(element, "border"),
            BorderColor = WordJsonValues.GetString(element, "border_color"),
            OutlineLevel = WordJsonValues.GetInt(element, "outline_level"),
        };
    }

    /// <summary>
    /// Reads a length property in points, accepting units such as <c>0.5in</c>, <c>1cm</c> or <c>18pt</c>.
    /// </summary>
    /// <param name="element">The JSON object.</param>
    /// <param name="name">The property name.</param>
    /// <param name="referencePoints">What a percentage is a percentage of.</param>
    /// <returns>The length in points, or <see langword="null"/>.</returns>
    public static double? ReadLength(JsonElement element, string name, double referencePoints = 468)
    {
        if (!WordJsonValues.TryGet(element, name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && WordUnits.TryParseLength(value.GetString(), referencePoints, out var points)
            ? points
            : null;
    }
}
