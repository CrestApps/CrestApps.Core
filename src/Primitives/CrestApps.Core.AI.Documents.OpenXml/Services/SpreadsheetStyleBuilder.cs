using System.Globalization;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;

namespace CrestApps.Core.AI.Documents.OpenXml.Services;

/// <summary>
/// Accumulates the fonts, fills, borders, number formats, and cell formats a generated workbook needs,
/// de-duplicating them so a sheet with thousands of identically styled cells still carries one entry
/// each.
/// <para>
/// A spreadsheet references styles by index into these collections, so the order the entries are added
/// in is the contract. Several slots are fixed by the file format itself and are reserved up front.
/// </para>
/// </summary>
internal sealed class SpreadsheetStyleBuilder
{
    // Custom number formats must start above the ids the format reserves for its built-in formats.
    private const uint FirstCustomNumberFormatId = 164;

    private readonly Dictionary<string, uint> _numberFormats = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _fontIndexes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _fillIndexes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _borderIndexes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _cellFormatIndexes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _differentialIndexes = new(StringComparer.Ordinal);

    private readonly List<Font> _fonts = [];
    private readonly List<Fill> _fills = [];
    private readonly List<Border> _borders = [];
    private readonly List<CellFormat> _cellFormats = [];
    private readonly List<DifferentialFormat> _differentialFormats = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SpreadsheetStyleBuilder"/> class, reserving the
    /// slots the file format requires.
    /// </summary>
    public SpreadsheetStyleBuilder()
    {
        _fonts.Add(CreateFont(null));
        _fontIndexes[string.Empty] = 0;

        // The first two fills are fixed by the format: index 0 must be None and index 1 must be
        // Gray125. Writing anything else there makes every fill in the workbook render incorrectly.
        _fills.Add(new Fill(new PatternFill { PatternType = PatternValues.None }));
        _fills.Add(new Fill(new PatternFill { PatternType = PatternValues.Gray125 }));

        _borders.Add(new Border(
            new LeftBorder(),
            new RightBorder(),
            new TopBorder(),
            new BottomBorder(),
            new DiagonalBorder()));
        _borderIndexes[string.Empty] = 0;

        _cellFormats.Add(new CellFormat
        {
            NumberFormatId = 0,
            FontId = 0,
            FillId = 0,
            BorderId = 0,
        });
        _cellFormatIndexes[string.Empty] = 0;
    }

    /// <summary>
    /// Resolves the cell format index for a style and number format combination, creating it on first
    /// use.
    /// </summary>
    /// <param name="style">The cell style. May be <see langword="null"/>.</param>
    /// <param name="numberFormatCode">The number format code. May be <see langword="null"/>.</param>
    /// <returns>The index to reference from a cell.</returns>
    public uint GetCellFormat(SpreadsheetCellStyle style, string numberFormatCode)
    {
        var key = BuildKey(style, numberFormatCode);

        if (_cellFormatIndexes.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var numberFormatId = GetNumberFormatId(numberFormatCode);
        var fontId = GetFontId(style);
        var fillId = GetFillId(style?.BackgroundColor);
        var borderId = style?.Border == true ? GetThinBorderId() : 0;

        var cellFormat = new CellFormat
        {
            NumberFormatId = numberFormatId,
            FontId = fontId,
            FillId = fillId,
            BorderId = borderId,
            ApplyNumberFormat = BooleanValue.FromBoolean(numberFormatId != 0),
            ApplyFont = BooleanValue.FromBoolean(fontId != 0),
            ApplyFill = BooleanValue.FromBoolean(fillId != 0),
            ApplyBorder = BooleanValue.FromBoolean(borderId != 0),
        };

        var alignment = BuildAlignment(style);

        if (alignment is not null)
        {
            cellFormat.Alignment = alignment;
            cellFormat.ApplyAlignment = true;
        }

        var index = (uint)_cellFormats.Count;
        _cellFormats.Add(cellFormat);
        _cellFormatIndexes[key] = index;

        return index;
    }

    /// <summary>
    /// Resolves the differential format index used by a conditional formatting rule. Conditional rules
    /// reference a separate collection from ordinary cells, because a rule only overrides the
    /// properties it names and leaves the rest of the cell's own formatting intact.
    /// </summary>
    /// <param name="style">The style applied when the rule matches.</param>
    /// <returns>The index to reference from a rule.</returns>
    public uint GetDifferentialFormat(SpreadsheetCellStyle style)
    {
        var key = BuildKey(style, numberFormatCode: null);

        if (_differentialIndexes.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var differential = new DifferentialFormat();

        if (style is not null)
        {
            var font = new Font();
            var hasFont = false;

            if (style.Bold == true)
            {
                font.Append(new Bold());
                hasFont = true;
            }

            if (style.Italic == true)
            {
                font.Append(new Italic());
                hasFont = true;
            }

            if (TryParseColor(style.FontColor, out var fontColor))
            {
                font.Append(new Color { Rgb = fontColor });
                hasFont = true;
            }

            if (hasFont)
            {
                differential.Append(font);
            }

            if (TryParseColor(style.BackgroundColor, out var background))
            {
                // A differential fill sets the background through the pattern's BackgroundColor, the
                // reverse of an ordinary cell fill. Using ForegroundColor here silently renders solid
                // black in Excel.
                differential.Append(new Fill(new PatternFill
                {
                    BackgroundColor = new BackgroundColor { Rgb = background },
                }));
            }
        }

        var index = (uint)_differentialFormats.Count;
        _differentialFormats.Add(differential);
        _differentialIndexes[key] = index;

        return index;
    }

    /// <summary>
    /// Builds the stylesheet describing every style registered so far.
    /// </summary>
    /// <returns>The stylesheet.</returns>
    public Stylesheet Build()
    {
        var stylesheet = new Stylesheet();

        if (_numberFormats.Count > 0)
        {
            var numberingFormats = new NumberingFormats { Count = (uint)_numberFormats.Count };

            foreach (var (code, id) in _numberFormats.OrderBy(entry => entry.Value))
            {
                numberingFormats.Append(new NumberingFormat
                {
                    NumberFormatId = id,
                    FormatCode = code,
                });
            }

            stylesheet.Append(numberingFormats);
        }

        stylesheet.Append(new Fonts(_fonts.Select(font => font.CloneNode(deep: true))) { Count = (uint)_fonts.Count });
        stylesheet.Append(new Fills(_fills.Select(fill => fill.CloneNode(deep: true))) { Count = (uint)_fills.Count });
        stylesheet.Append(new Borders(_borders.Select(border => border.CloneNode(deep: true))) { Count = (uint)_borders.Count });

        stylesheet.Append(new CellStyleFormats(new CellFormat
        {
            NumberFormatId = 0,
            FontId = 0,
            FillId = 0,
            BorderId = 0,
        })
        {
            Count = 1,
        });

        stylesheet.Append(new CellFormats(_cellFormats.Select(format => format.CloneNode(deep: true))) { Count = (uint)_cellFormats.Count });

        stylesheet.Append(new CellStyles(new CellStyle
        {
            Name = "Normal",
            FormatId = 0,
            BuiltinId = 0,
        })
        {
            Count = 1,
        });

        if (_differentialFormats.Count > 0)
        {
            stylesheet.Append(new DifferentialFormats(
                _differentialFormats.Select(format => format.CloneNode(deep: true)))
            {
                Count = (uint)_differentialFormats.Count,
            });
        }

        return stylesheet;
    }

    private uint GetNumberFormatId(string numberFormatCode)
    {
        if (string.IsNullOrWhiteSpace(numberFormatCode))
        {
            return 0;
        }

        var code = numberFormatCode.Trim();

        if (_numberFormats.TryGetValue(code, out var existing))
        {
            return existing;
        }

        var id = FirstCustomNumberFormatId + (uint)_numberFormats.Count;
        _numberFormats[code] = id;

        return id;
    }

    private uint GetFontId(SpreadsheetCellStyle style)
    {
        var key = style is null
            ? string.Empty
            : string.Join(
                '|',
                style.Bold == true ? "b" : string.Empty,
                style.Italic == true ? "i" : string.Empty,
                style.Underline == true ? "u" : string.Empty,
                style.FontSize?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                style.FontName ?? string.Empty,
                style.FontColor ?? string.Empty);

        if (_fontIndexes.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var index = (uint)_fonts.Count;
        _fonts.Add(CreateFont(style));
        _fontIndexes[key] = index;

        return index;
    }

    private uint GetFillId(string backgroundColor)
    {
        if (!TryParseColor(backgroundColor, out var color))
        {
            return 0;
        }

        if (_fillIndexes.TryGetValue(color, out var existing))
        {
            return existing;
        }

        var index = (uint)_fills.Count;

        _fills.Add(new Fill(new PatternFill
        {
            PatternType = PatternValues.Solid,
            ForegroundColor = new ForegroundColor { Rgb = color },
            BackgroundColor = new BackgroundColor { Indexed = 64U },
        }));

        _fillIndexes[color] = index;

        return index;
    }

    private uint GetThinBorderId()
    {
        const string Key = "thin";

        if (_borderIndexes.TryGetValue(Key, out var existing))
        {
            return existing;
        }

        var index = (uint)_borders.Count;

        _borders.Add(new Border(
            new LeftBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
            new RightBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
            new TopBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
            new BottomBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
            new DiagonalBorder()));

        _borderIndexes[Key] = index;

        return index;
    }

    private static Font CreateFont(SpreadsheetCellStyle style)
    {
        var font = new Font();

        if (style?.Bold == true)
        {
            font.Append(new Bold());
        }

        if (style?.Italic == true)
        {
            font.Append(new Italic());
        }

        if (style?.Underline == true)
        {
            font.Append(new Underline());
        }

        font.Append(new FontSize { Val = style?.FontSize ?? 11D });

        if (TryParseColor(style?.FontColor, out var color))
        {
            font.Append(new Color { Rgb = color });
        }

        font.Append(new FontName { Val = string.IsNullOrWhiteSpace(style?.FontName) ? "Calibri" : style.FontName.Trim() });

        return font;
    }

    private static Alignment BuildAlignment(SpreadsheetCellStyle style)
    {
        if (style is null)
        {
            return null;
        }

        var horizontal = style.Alignment switch
        {
            SpreadsheetHorizontalAlignment.Left => HorizontalAlignmentValues.Left,
            SpreadsheetHorizontalAlignment.Center => HorizontalAlignmentValues.Center,
            SpreadsheetHorizontalAlignment.Right => HorizontalAlignmentValues.Right,
            _ => (HorizontalAlignmentValues?)null,
        };

        if (horizontal is null && style.WrapText != true)
        {
            return null;
        }

        var alignment = new Alignment();

        if (horizontal is not null)
        {
            alignment.Horizontal = horizontal.Value;
        }

        if (style.WrapText == true)
        {
            alignment.WrapText = true;
        }

        return alignment;
    }

    private static string BuildKey(SpreadsheetCellStyle style, string numberFormatCode)
    {
        if (style is null)
        {
            return string.IsNullOrWhiteSpace(numberFormatCode)
                ? string.Empty
                : "#" + numberFormatCode.Trim();
        }

        return string.Join(
            '|',
            numberFormatCode?.Trim() ?? string.Empty,
            style.Bold == true ? "b" : string.Empty,
            style.Italic == true ? "i" : string.Empty,
            style.Underline == true ? "u" : string.Empty,
            style.FontSize?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            style.FontName ?? string.Empty,
            style.FontColor ?? string.Empty,
            style.BackgroundColor ?? string.Empty,
            style.Alignment.ToString(),
            style.WrapText == true ? "w" : string.Empty,
            style.Border == true ? "x" : string.Empty);
    }

    /// <summary>
    /// Parses a color into the eight-digit alpha-red-green-blue form the file format stores. Accepts
    /// <c>#RRGGBB</c>, <c>RRGGBB</c>, and <c>AARRGGBB</c>, with or without the leading hash.
    /// </summary>
    /// <param name="value">The color to parse.</param>
    /// <param name="color">The parsed color.</param>
    /// <returns><see langword="true"/> when the value is a usable color.</returns>
    public static bool TryParseColor(string value, out string color)
    {
        color = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim().TrimStart('#');

        foreach (var character in text)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        color = text.Length switch
        {
            6 => "FF" + text.ToUpperInvariant(),
            8 => text.ToUpperInvariant(),
            _ => null,
        };

        return color is not null;
    }
}
