using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace CrestApps.Core.Tests.Support;

/// <summary>
/// Builds synthetic PDF fixtures in memory with PdfPig's writer so no binary document is ever committed.
/// Coordinates are PDF user-space points with the origin at the bottom-left corner of the page; an A4 page
/// is 595 by 842 points.
/// </summary>
internal sealed class PdfFixtureBuilder
{
    private readonly PdfDocumentBuilder _builder = new();
    private readonly PdfDocumentBuilder.AddedFont _regular;
    private readonly PdfDocumentBuilder.AddedFont _bold;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfFixtureBuilder"/> class.
    /// </summary>
    public PdfFixtureBuilder()
    {
        _regular = _builder.AddStandard14Font(Standard14Font.Helvetica);
        _bold = _builder.AddStandard14Font(Standard14Font.HelveticaBold);
    }

    /// <summary>
    /// Appends an A4 page and lets the caller draw on it.
    /// </summary>
    /// <param name="configure">The callback that draws the page content.</param>
    /// <returns>The same builder, so pages can be chained.</returns>
    public PdfFixtureBuilder Page(Action<PdfFixturePage> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var page = _builder.AddPage(PageSize.A4);
        configure(new PdfFixturePage(page, _regular, _bold));

        return this;
    }

    /// <summary>
    /// Appends a page of the requested size and lets the caller draw on it.
    /// </summary>
    /// <param name="width">The page width, in points.</param>
    /// <param name="height">The page height, in points.</param>
    /// <param name="configure">The callback that draws the page content.</param>
    /// <returns>The same builder, so pages can be chained.</returns>
    public PdfFixtureBuilder Page(double width, double height, Action<PdfFixturePage> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var page = _builder.AddPage(width, height);
        configure(new PdfFixturePage(page, _regular, _bold));

        return this;
    }

    /// <summary>
    /// Renders the document.
    /// </summary>
    /// <returns>A readable stream positioned at the start of the PDF bytes.</returns>
    public MemoryStream Build()
    {
        return new MemoryStream(_builder.Build());
    }

    /// <summary>
    /// Renders the document as raw bytes.
    /// </summary>
    /// <returns>The PDF bytes.</returns>
    public byte[] BuildBytes()
    {
        return _builder.Build();
    }
}

/// <summary>
/// Draws content onto one fixture page. Every position is the lower-left corner of the drawn element in PDF
/// user space, so a larger <c>y</c> is higher on the page.
/// </summary>
internal sealed class PdfFixturePage
{
    private readonly PdfPageBuilder _page;
    private readonly PdfDocumentBuilder.AddedFont _regular;
    private readonly PdfDocumentBuilder.AddedFont _bold;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfFixturePage"/> class.
    /// </summary>
    /// <param name="page">The underlying page builder.</param>
    /// <param name="regular">The regular font.</param>
    /// <param name="bold">The bold font, used for headings.</param>
    public PdfFixturePage(
        PdfPageBuilder page,
        PdfDocumentBuilder.AddedFont regular,
        PdfDocumentBuilder.AddedFont bold)
    {
        _page = page;
        _regular = regular;
        _bold = bold;
    }

    /// <summary>
    /// Draws one line of text with its baseline at the supplied position.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="x">The baseline start, in points from the left edge.</param>
    /// <param name="y">The baseline height, in points from the bottom edge.</param>
    /// <param name="size">The font size, in points.</param>
    /// <param name="bold">Whether the bold face is used.</param>
    /// <returns>The same page, so calls can be chained.</returns>
    public PdfFixturePage Text(string text, double x, double y, double size = 11, bool bold = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        _page.AddText(text, size, new PdfPoint(x, y), bold ? _bold : _regular);

        return this;
    }

    /// <summary>
    /// Places a PNG image on the page.
    /// </summary>
    /// <param name="png">The PNG bytes.</param>
    /// <param name="x">The left edge, in points from the left of the page.</param>
    /// <param name="y">The bottom edge, in points from the bottom of the page.</param>
    /// <param name="width">The drawn width, in points.</param>
    /// <param name="height">The drawn height, in points.</param>
    /// <returns>The same page, so calls can be chained.</returns>
    public PdfFixturePage Png(byte[] png, double x, double y, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(png);

        _page.AddPng(png, new PdfRectangle(x, y, x + width, y + height));

        return this;
    }

    /// <summary>
    /// Draws a straight line.
    /// </summary>
    /// <param name="fromX">The start, in points from the left edge.</param>
    /// <param name="fromY">The start, in points from the bottom edge.</param>
    /// <param name="toX">The end, in points from the left edge.</param>
    /// <param name="toY">The end, in points from the bottom edge.</param>
    /// <param name="lineWidth">The stroke width, in points.</param>
    /// <returns>The same page, so calls can be chained.</returns>
    public PdfFixturePage Line(double fromX, double fromY, double toX, double toY, double lineWidth = 1)
    {
        _page.DrawLine(new PdfPoint(fromX, fromY), new PdfPoint(toX, toY), lineWidth);

        return this;
    }

    /// <summary>
    /// Draws a rectangle.
    /// </summary>
    /// <param name="x">The left edge, in points from the left of the page.</param>
    /// <param name="y">The bottom edge, in points from the bottom of the page.</param>
    /// <param name="width">The width, in points.</param>
    /// <param name="height">The height, in points.</param>
    /// <param name="lineWidth">The stroke width, in points.</param>
    /// <param name="fill">Whether the rectangle is filled.</param>
    /// <returns>The same page, so calls can be chained.</returns>
    public PdfFixturePage Rectangle(double x, double y, double width, double height, double lineWidth = 1, bool fill = false)
    {
        _page.DrawRectangle(new PdfPoint(x, y), width, height, lineWidth, fill);

        return this;
    }
}
