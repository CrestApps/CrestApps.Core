using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;
using CrestApps.Core.AI.Documents.Pdf.Graphics.Images;
using CrestApps.Core.AI.Documents.Pdf.Objects;

namespace CrestApps.Core.AI.Documents.Pdf.Graphics;

/// <summary>
/// Draws on a page or into a form object: shapes, text and pictures, written as a content stream.
/// </summary>
/// <remarks>
/// Coordinates are in points from the top-left corner, with y growing downwards, the way a page is read.
/// Nothing is written to the page or form until <see cref="Commit"/>; drawn over a page, the existing
/// content is wrapped so the state it leaves behind cannot shift what is drawn.
/// </remarks>
internal sealed class PdfCanvas
{
    private readonly PdfDocument _document;
    private readonly PdfDictionary _resources;
    private readonly Action<byte[]> _commit;
    private readonly StringBuilder _content = new();
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private int _depth;
    private bool _committed;

    private PdfCanvas(PdfDocument document, PdfDictionary resources, double width, double height, string transform, Action<byte[]> commit)
    {
        _document = document;
        _resources = resources;
        _commit = commit;
        Width = width;
        Height = height;
        _content.Append(transform).Append(" cm\n");
    }

    /// <summary>
    /// Gets the width of the area drawn on, in points.
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// Gets the height of the area drawn on, in points.
    /// </summary>
    public double Height { get; }

    /// <summary>
    /// Creates a canvas over a page's media box.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="behind">Whether to draw behind the page's content rather than over it.</param>
    /// <returns>The canvas.</returns>
    public static PdfCanvas ForPage(PdfPage page, bool behind = false)
    {
        ArgumentNullException.ThrowIfNull(page);

        var media = page.MediaBox;
        var transform = Matrix(1, 0, 0, -1, media.X1, media.Y2);

        return new PdfCanvas(page.Owner, page.Resources, media.Width, media.Height, transform, content =>
        {
            if (behind)
            {
                page.Contents.PrependContent().Stream.SetCompressed(Wrap(content));

                return;
            }

            page.Contents.PrependContent().Stream.SetUncompressed(Encoding.ASCII.GetBytes("q\n"));
            page.Contents.AppendContent().Stream.SetCompressed([.. Encoding.ASCII.GetBytes("Q\n"), .. Wrap(content)]);
        });
    }

    /// <summary>
    /// Creates a canvas that draws a form object: a drawing a page shows with <see cref="DrawForm"/>, or an
    /// annotation's appearance.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="width">The form's width, in points.</param>
    /// <param name="height">The form's height, in points.</param>
    /// <param name="form">The form object; it holds the drawing once the canvas is committed.</param>
    /// <returns>The canvas.</returns>
    public static PdfCanvas ForForm(PdfDocument document, double width, double height, out PdfDictionary form)
    {
        ArgumentNullException.ThrowIfNull(document);

        var created = new PdfDictionary(document);
        var resources = new PdfDictionary(document);

        created.Elements.SetName("/Type", "/XObject");
        created.Elements.SetName("/Subtype", "/Form");
        created.Elements["/BBox"] = new PdfRectangle(0, 0, width, height);
        created.Elements["/Resources"] = resources;
        created.CreateStream([]);
        document.AddObject(created);
        form = created;

        return new PdfCanvas(document, resources, width, height, Matrix(1, 0, 0, -1, 0, height), content => created.Stream.SetCompressed(content));
    }

    /// <summary>
    /// Writes what was drawn. A canvas is committed once; drawing after that is not written.
    /// </summary>
    public void Commit()
    {
        if (_committed)
        {
            return;
        }

        _committed = true;

        while (_depth > 0)
        {
            Restore();
        }

        _commit(Encoding.Latin1.GetBytes(_content.ToString()));
    }

    /// <summary>
    /// Saves the graphics state: the transformation, the clipping and the colours.
    /// </summary>
    public void Save()
    {
        _depth++;
        _content.Append("q\n");
    }

    /// <summary>
    /// Restores the graphics state saved last.
    /// </summary>
    public void Restore()
    {
        if (_depth == 0)
        {
            return;
        }

        _depth--;
        _content.Append("Q\n");
    }

    /// <summary>
    /// Changes the coordinates with a matrix: x' = a·x + c·y + e, y' = b·x + d·y + f.
    /// </summary>
    /// <param name="a">The x scale.</param>
    /// <param name="b">The y shear.</param>
    /// <param name="c">The x shear.</param>
    /// <param name="d">The y scale.</param>
    /// <param name="e">The x offset.</param>
    /// <param name="f">The y offset.</param>
    public void Transform(double a, double b, double c, double d, double e, double f)
    {
        _content.Append(Matrix(a, b, c, d, e, f)).Append(" cm\n");
    }

    /// <summary>
    /// Moves the origin.
    /// </summary>
    /// <param name="x">The distance right.</param>
    /// <param name="y">The distance down.</param>
    public void Translate(double x, double y)
    {
        Transform(1, 0, 0, 1, x, y);
    }

    /// <summary>
    /// Scales the coordinates.
    /// </summary>
    /// <param name="x">The horizontal scale.</param>
    /// <param name="y">The vertical scale.</param>
    public void Scale(double x, double y)
    {
        Transform(x, 0, 0, y, 0, 0);
    }

    /// <summary>
    /// Rotates the coordinates clockwise around a point.
    /// </summary>
    /// <param name="degrees">The angle, clockwise.</param>
    /// <param name="x">The x of the point turned around.</param>
    /// <param name="y">The y of the point turned around.</param>
    public void RotateAt(double degrees, double x, double y)
    {
        var radians = degrees * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        Transform(cos, sin, -sin, cos, x - (cos * x) + (sin * y), y - (sin * x) - (cos * y));
    }

    /// <summary>
    /// Limits what is drawn next, until the state is restored, to the inside of a path.
    /// </summary>
    /// <param name="path">The path.</param>
    public void Clip(PdfPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.IsEmpty)
        {
            return;
        }

        var clip = path.EvenOdd
            ? "W* n\n"
            : "W n\n";

        _content.Append(path).Append(clip);
    }

    /// <summary>
    /// Fills a path, strokes it, or both.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="brush">How to fill it, or <see langword="null"/> to leave it unfilled.</param>
    /// <param name="pen">How to stroke it, or <see langword="null"/> to leave it unstroked.</param>
    public void DrawPath(PdfPath path, PdfBrush brush, PdfPen pen = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.IsEmpty || (brush is null && pen is null))
        {
            return;
        }

        if (brush?.EndColor is not null)
        {
            // A gradient fills the inside of the path through a clip; the outline is stroked over it.
            Save();
            Clip(path);
            SetOpacity(brush.Opacity, 1);
            _content.Append('/').Append(Shading(brush)).Append(" sh\n");
            Restore();

            if (pen is not null)
            {
                DrawPath(path, null, pen);
            }

            return;
        }

        Save();
        SetOpacity(brush?.Opacity ?? 1, pen?.Opacity ?? 1);

        if (brush is not null)
        {
            _content.Append(Color(brush.Color)).Append(" rg\n");
        }

        if (pen is not null)
        {
            ApplyPen(pen);
        }

        _content.Append(path);

        var paint = (brush is not null, pen is not null, path.EvenOdd) switch
        {
            (true, true, true) => "B*",
            (true, true, false) => "B",
            (true, false, true) => "f*",
            (true, false, false) => "f",
            _ => "S",
        };

        _content.Append(paint).Append('\n');
        Restore();
    }

    /// <summary>
    /// Fills a rectangle, strokes it, or both.
    /// </summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="brush">How to fill it, or <see langword="null"/>.</param>
    /// <param name="pen">How to stroke it, or <see langword="null"/>.</param>
    public void DrawRectangle(double x, double y, double width, double height, PdfBrush brush, PdfPen pen = null)
    {
        DrawPath(new PdfPath().AddRectangle(x, y, width, height), brush, pen);
    }

    /// <summary>
    /// Draws a straight line.
    /// </summary>
    /// <param name="x1">The x it starts at.</param>
    /// <param name="y1">The y it starts at.</param>
    /// <param name="x2">The x it ends at.</param>
    /// <param name="y2">The y it ends at.</param>
    /// <param name="pen">How to draw it.</param>
    public void DrawLine(double x1, double y1, double x2, double y2, PdfPen pen)
    {
        ArgumentNullException.ThrowIfNull(pen);

        DrawPath(new PdfPath().MoveTo(x1, y1).LineTo(x2, y2), null, pen);
    }

    /// <summary>
    /// Writes a line of text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="font">The font.</param>
    /// <param name="size">The font size, in points.</param>
    /// <param name="x">The x the text starts at.</param>
    /// <param name="baseline">The y of the baseline.</param>
    /// <param name="brush">The colour.</param>
    /// <param name="invisible">Whether the text is written without being shown, so it can only be selected and searched.</param>
    public void DrawText(string text, PdfFont font, double size, double x, double baseline, PdfBrush brush, bool invisible = false)
    {
        ArgumentNullException.ThrowIfNull(font);

        if (string.IsNullOrEmpty(text) || size <= 0)
        {
            return;
        }

        var codes = font.Encode(text);
        var dictionary = PdfDocumentFonts.For(_document).Use(font, codes);
        var name = Resource("/Font", dictionary, "F");
        var color = brush?.Color ?? new PdfColor(0, 0, 0);
        var simulateBold = font.SimulatesBold && !invisible;

        // Italic is simulated by slanting the text a fifth of its height.
        var slant = font.SimulatesItalic
            ? 0.2
            : 0;

        Save();
        SetOpacity(brush?.Opacity ?? 1, brush?.Opacity ?? 1);
        _content.Append("BT\n/").Append(name).Append(' ').Append(PdfObjectModel.Format(size)).Append(" Tf\n");
        _content.Append(Matrix(1, 0, slant, -1, x, baseline)).Append(" Tm\n");
        _content.Append(Color(color)).Append(" rg\n");

        if (invisible)
        {
            _content.Append("3 Tr\n");
        }
        else if (simulateBold)
        {
            // Bold is simulated by stroking the outlines in the text colour as well as filling them.
            _content.Append(Color(color)).Append(" RG\n").Append(PdfObjectModel.Format(size / 30)).Append(" w\n2 Tr\n");
        }

        // A TrueType font writes two-byte glyph codes, a standard font one-byte character codes.
        var codeFormat = font.IsEmbedded
            ? "X4"
            : "X2";

        _content.Append('<');

        foreach (var code in codes)
        {
            _content.Append(code.Code.ToString(codeFormat, CultureInfo.InvariantCulture));
        }

        _content.Append("> Tj\nET\n");
        Restore();
    }

    /// <summary>
    /// Draws a picture into a box.
    /// </summary>
    /// <param name="image">The picture.</param>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="opacity">The opacity.</param>
    public void DrawImage(PdfImage image, double x, double y, double width, double height, double opacity = 1)
    {
        ArgumentNullException.ThrowIfNull(image);

        var name = Resource("/XObject", image.GetXObject(_document), "Im");

        Save();
        SetOpacity(opacity, opacity);

        // The picture fills the unit square with its first row at the top; the canvas's y runs downwards.
        Transform(width, 0, 0, -height, x, y + height);
        _content.Append('/').Append(name).Append(" Do\n");
        Restore();
    }

    /// <summary>
    /// Draws a form object into a box, scaled from the form's bounding box.
    /// </summary>
    /// <param name="form">The form object.</param>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="opacity">The opacity.</param>
    public void DrawForm(PdfDictionary form, double x, double y, double width, double height, double opacity = 1)
    {
        ArgumentNullException.ThrowIfNull(form);

        var box = PdfRectangle.From(form.Elements["/BBox"]) ?? new PdfRectangle(0, 0, width, height);
        var name = Resource("/XObject", form, "Fm");
        var scaleX = box.Width > 0
            ? width / box.Width
            : 1;

        var scaleY = box.Height > 0
            ? height / box.Height
            : 1;


        Save();
        SetOpacity(opacity, opacity);
        Transform(scaleX, 0, 0, -scaleY, x - (box.X1 * scaleX), y + height + (box.Y1 * scaleY));
        _content.Append('/').Append(name).Append(" Do\n");
        Restore();
    }

    /// <summary>
    /// Writes content stream operators as they are, for what the canvas has no method for.
    /// </summary>
    /// <param name="operators">The operators, in the canvas's coordinates.</param>
    public void DrawRaw(string operators)
    {
        if (!string.IsNullOrEmpty(operators))
        {
            _content.Append(operators).Append('\n');
        }
    }

    private static byte[] Wrap(byte[] content)
    {
        return [.. Encoding.ASCII.GetBytes("q\n"), .. content, .. Encoding.ASCII.GetBytes("Q\n")];
    }

    private static string Matrix(double a, double b, double c, double d, double e, double f)
    {
        return string.Join(' ', new[] { a, b, c, d, e, f }.Select(PdfObjectModel.Format));
    }

    private static string Color(PdfColor color)
    {
        return PdfObjectModel.Format(color.Red / 255.0) + " " + PdfObjectModel.Format(color.Green / 255.0) + " " + PdfObjectModel.Format(color.Blue / 255.0);
    }

    private void ApplyPen(PdfPen pen)
    {
        _content.Append(Color(pen.Color)).Append(" RG\n").Append(PdfObjectModel.Format(Math.Max(0, pen.Width))).Append(" w\n");

        if (pen.RoundJoins)
        {
            _content.Append("1 j 1 J\n");
        }

        if (pen.Dash is { Count: > 0 })
        {
            _content.Append('[').Append(string.Join(' ', pen.Dash.Select(PdfObjectModel.Format))).Append("] 0 d\n");
        }
    }

    private void SetOpacity(double fill, double stroke)
    {
        fill = Math.Clamp(fill, 0, 1);
        stroke = Math.Clamp(stroke, 0, 1);

        if (fill >= 1 && stroke >= 1)
        {
            return;
        }

        var key = "/ExtGState:" + PdfObjectModel.Format(fill) + "/" + PdfObjectModel.Format(stroke);

        if (!_names.TryGetValue(key, out var name))
        {
            var state = new PdfDictionary(_document);

            state.Elements.SetName("/Type", "/ExtGState");
            state.Elements.SetReal("/ca", fill);
            state.Elements.SetReal("/CA", stroke);
            name = Resource("/ExtGState", state, "GS");
            _names[key] = name;
        }

        _content.Append('/').Append(name).Append(" gs\n");
    }

    private string Shading(PdfBrush brush)
    {
        var function = new PdfDictionary(_document);
        var shading = new PdfDictionary(_document);
        var end = brush.EndColor.Value;
        var (x1, y1, x2, y2) = brush.Axis;

        function.Elements.SetInteger("/FunctionType", 2);
        function.Elements["/Domain"] = new PdfArray(_document, new PdfInteger(0), new PdfInteger(1));
        function.Elements["/C0"] = Components(brush.Color);
        function.Elements["/C1"] = Components(end);
        function.Elements.SetInteger("/N", 1);

        shading.Elements.SetName("/ColorSpace", "/DeviceRGB");
        shading.Elements["/Function"] = function;
        shading.Elements["/Extend"] = new PdfArray(_document, PdfBoolean.True, PdfBoolean.True);

        if (brush.IsRadial)
        {
            shading.Elements.SetInteger("/ShadingType", 3);
            shading.Elements["/Coords"] = new PdfArray(_document, new[] { x1, y1, 0, x1, y1, Math.Max(0.01, brush.Radius) }.Select(value => new PdfReal(value)));
        }
        else
        {
            shading.Elements.SetInteger("/ShadingType", 2);
            shading.Elements["/Coords"] = new PdfArray(_document, new[] { x1, y1, x2, y2 }.Select(value => new PdfReal(value)));
        }

        return Resource("/Shading", shading, "Sh");
    }

    private PdfArray Components(PdfColor color)
    {
        return new PdfArray(_document, new PdfReal(color.Red / 255.0), new PdfReal(color.Green / 255.0), new PdfReal(color.Blue / 255.0));
    }

    /// <summary>
    /// Adds an object to the resources under a name the page does not use yet, or returns the name it already has.
    /// </summary>
    private string Resource(string category, PdfDictionary value, string prefix)
    {
        var key = category + ":" + _document.AddObject(value).ObjectNumber.ToString(CultureInfo.InvariantCulture);

        if (_names.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var group = _resources.Elements.GetDictionary(category);

        if (group is null)
        {
            group = new PdfDictionary(_document);
            _resources.Elements[category] = group;
        }

        var index = 1;
        string name;

        do
        {
            name = "Ca" + prefix + index.ToString(CultureInfo.InvariantCulture);
            index++;
        }
        while (group.Elements.ContainsKey("/" + name));

        group.Elements["/" + name] = value.Reference;
        _names[key] = name;

        return name;
    }
}
