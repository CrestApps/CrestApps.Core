namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// A theme with every default filled in and every colour and font checked, ready to draw with.
/// </summary>
internal sealed class PdfResolvedTheme
{
    /// <summary>
    /// The brand colour used when none is given: the dark blue the spreadsheet export uses for its header, so
    /// a report delivered as a workbook and as a PDF looks like the same report.
    /// </summary>
    public static readonly PdfColor DefaultPrimary = new(0x1F, 0x4E, 0x79);

    private static readonly string[] _defaultChartColors =
    [
        "#1F4E79",
        "#2E75B6",
        "#70AD47",
        "#ED7D31",
        "#FFC000",
        "#7030A0",
        "#C00000",
        "#44546A",
        "#00B0F0",
        "#A5A5A5",
    ];

    /// <summary>
    /// Gets the brand colour.
    /// </summary>
    public PdfColor Primary { get; private init; }

    /// <summary>
    /// Gets the accent colour.
    /// </summary>
    public PdfColor Accent { get; private init; }

    /// <summary>
    /// Gets the body text colour.
    /// </summary>
    public PdfColor Text { get; private init; }

    /// <summary>
    /// Gets the heading colour.
    /// </summary>
    public PdfColor Heading { get; private init; }

    /// <summary>
    /// Gets the colour of secondary text.
    /// </summary>
    public PdfColor Muted { get; private init; }

    /// <summary>
    /// Gets the body font family.
    /// </summary>
    public string FontFamily { get; private init; }

    /// <summary>
    /// Gets the heading font family.
    /// </summary>
    public string HeadingFontFamily { get; private init; }

    /// <summary>
    /// Gets the body font size in points.
    /// </summary>
    public double BaseFontSize { get; private init; }

    /// <summary>
    /// Gets the line spacing multiple.
    /// </summary>
    public double LineSpacing { get; private init; }

    /// <summary>
    /// Gets the logo source, or <see langword="null"/>.
    /// </summary>
    public string Logo { get; private init; }

    /// <summary>
    /// Gets where the logo is printed on body pages.
    /// </summary>
    public string LogoPosition { get; private init; }

    /// <summary>
    /// Gets the logo height in points.
    /// </summary>
    public double LogoHeight { get; private init; }

    /// <summary>
    /// Gets the chart series colours, in order.
    /// </summary>
    public IReadOnlyList<PdfColor> ChartColors { get; private init; }

    /// <summary>
    /// Gets the table header fill.
    /// </summary>
    public PdfColor TableHeaderBackground { get; private init; }

    /// <summary>
    /// Gets the table header text colour.
    /// </summary>
    public PdfColor TableHeaderText { get; private init; }

    /// <summary>
    /// Gets the table border colour.
    /// </summary>
    public PdfColor TableBorder { get; private init; }

    /// <summary>
    /// Gets a value indicating whether tables are banded by default.
    /// </summary>
    public bool TableBanded { get; private init; }

    /// <summary>
    /// Gets the band fill.
    /// </summary>
    public PdfColor TableBand { get; private init; }

    /// <summary>
    /// Gets the table font size, or <see langword="null"/> to size by column count.
    /// </summary>
    public double? TableFontSize { get; private init; }

    /// <summary>
    /// Gets which table borders are drawn by default.
    /// </summary>
    public string TableBorders { get; private init; }

    /// <summary>
    /// Resolves a theme.
    /// </summary>
    /// <param name="theme">The theme as described, or <see langword="null"/>.</param>
    /// <param name="defaultFontFamily">The host's default font family.</param>
    /// <param name="warnings">Receives notes about values that could not be honoured.</param>
    /// <returns>The resolved theme.</returns>
    public static PdfResolvedTheme Resolve(PdfThemeDefinition theme, string defaultFontFamily, List<string> warnings)
    {
        theme ??= new PdfThemeDefinition();

        var primary = ReadColor(theme.PrimaryColor, DefaultPrimary, "primary_color", warnings);
        var accent = ReadColor(theme.AccentColor, new PdfColor(0x2E, 0x75, 0xB6), "accent_color", warnings);
        var fontFamily = PdfFontFamilies.Resolve(theme.FontFamily, PdfFontFamilies.Resolve(defaultFontFamily, PdfFontFamilies.Default, null), warnings);
        var tables = theme.Tables ?? new PdfTableStyleDefinition();
        var headerBackground = ReadColor(tables.HeaderBackground, primary, "tables.header_background", warnings);

        var chartColors = new List<PdfColor>();

        foreach (var color in theme.ChartColors ?? [])
        {
            if (PdfColor.TryParse(color, out var parsed))
            {
                chartColors.Add(parsed);
            }
        }

        if (chartColors.Count == 0)
        {
            // Led by the brand colours so a themed document's charts match its headings.
            chartColors.Add(primary);

            if (!accent.Equals(primary))
            {
                chartColors.Add(accent);
            }

            foreach (var color in _defaultChartColors)
            {
                var parsed = PdfColor.Parse(color, primary);

                if (!chartColors.Contains(parsed))
                {
                    chartColors.Add(parsed);
                }
            }
        }

        return new PdfResolvedTheme
        {
            Primary = primary,
            Accent = accent,
            Text = ReadColor(theme.TextColor, new PdfColor(0x1F, 0x29, 0x33), "text_color", warnings),
            Heading = ReadColor(theme.HeadingColor, primary, "heading_color", warnings),
            Muted = ReadColor(theme.MutedColor, new PdfColor(0x5B, 0x66, 0x73), "muted_color", warnings),
            FontFamily = fontFamily,
            HeadingFontFamily = PdfFontFamilies.Resolve(theme.HeadingFontFamily, fontFamily, warnings),
            BaseFontSize = Math.Clamp(theme.BaseFontSize ?? 10.5, 6, 24),
            LineSpacing = Math.Clamp(theme.LineSpacing ?? 1.15, 0.8, 3),
            Logo = string.IsNullOrWhiteSpace(theme.Logo) ? null : theme.Logo.Trim(),
            LogoPosition = string.IsNullOrWhiteSpace(theme.LogoPosition) ? "header-left" : theme.LogoPosition.Trim().ToLowerInvariant(),
            LogoHeight = Math.Clamp(theme.LogoHeight ?? 24, 8, 120),
            ChartColors = chartColors,
            TableHeaderBackground = headerBackground,
            TableHeaderText = ReadColor(
                tables.HeaderTextColor,
                headerBackground.IsLight ? new PdfColor(0x1F, 0x29, 0x33) : new PdfColor(255, 255, 255),
                "tables.header_text_color",
                warnings),
            TableBorder = ReadColor(tables.BorderColor, new PdfColor(0xC8, 0xCF, 0xD8), "tables.border_color", warnings),
            TableBanded = tables.Banded ?? true,
            TableBand = ReadColor(tables.BandColor, new PdfColor(0xF3, 0xF5, 0xF8), "tables.band_color", warnings),
            TableFontSize = tables.FontSize is > 0 ? Math.Clamp(tables.FontSize.Value, 5, 16) : null,
            TableBorders = string.IsNullOrWhiteSpace(tables.Borders) ? "all" : tables.Borders.Trim().ToLowerInvariant(),
        };
    }

    /// <summary>
    /// Returns a copy of this theme drawn in one font family throughout.
    /// </summary>
    /// <param name="family">The family.</param>
    /// <returns>The theme with its fonts replaced.</returns>
    public PdfResolvedTheme WithFonts(string family)
    {
        return new PdfResolvedTheme
        {
            Primary = Primary,
            Accent = Accent,
            Text = Text,
            Heading = Heading,
            Muted = Muted,
            FontFamily = family,
            HeadingFontFamily = family,
            BaseFontSize = BaseFontSize,
            LineSpacing = LineSpacing,
            Logo = Logo,
            LogoPosition = LogoPosition,
            LogoHeight = LogoHeight,
            ChartColors = ChartColors,
            TableHeaderBackground = TableHeaderBackground,
            TableHeaderText = TableHeaderText,
            TableBorder = TableBorder,
            TableBanded = TableBanded,
            TableBand = TableBand,
            TableFontSize = TableFontSize,
            TableBorders = TableBorders,
        };
    }

    /// <summary>
    /// Gets the colour for a chart series by position.
    /// </summary>
    /// <param name="index">The series position.</param>
    /// <returns>The colour.</returns>
    public PdfColor ChartColor(int index)
    {
        return ChartColors[index % ChartColors.Count];
    }

    /// <summary>
    /// Reads a colour, noting a value that is not one.
    /// </summary>
    /// <param name="value">The colour as written.</param>
    /// <param name="fallback">The colour used when the value is missing or unreadable.</param>
    /// <param name="property">The property the value was given for, used in the note.</param>
    /// <param name="warnings">Receives the note.</param>
    /// <returns>The colour.</returns>
    public static PdfColor ReadColor(string value, PdfColor fallback, string property, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (PdfColor.TryParse(value, out var color))
        {
            return color;
        }

        warnings?.Add($"\"{value}\" given for {property} is not a colour; use a hex value such as #1F4E79.");

        return fallback;
    }
}
