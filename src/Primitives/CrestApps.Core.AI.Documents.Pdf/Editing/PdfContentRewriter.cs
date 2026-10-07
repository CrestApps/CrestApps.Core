using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Rewrites a page's content stream to take content out of it: glyphs and images inside given areas, text
/// drawn invisibly, or marked content belonging to an optional-content layer.
/// </summary>
/// <remarks>
/// This is what makes a redaction a redaction. A black box drawn over text leaves the text in the file, where
/// any reader can select, copy or extract it; the only way to remove it is to remove the operators that draw
/// it. The rewriter replays the content stream with its graphics and text state, works out where every glyph
/// lands from the font's metrics, and writes the stream back without the glyphs that land in an area —
/// replacing each removed run with the equivalent horizontal movement, so the text around it stays exactly
/// where it was.
/// <para>
/// Form XObjects are followed: one that draws into an area is rewritten as a private copy for this page, so
/// the same form used elsewhere is left intact. Images inside an area are removed whole, because nothing in
/// this process can decode and repaint their pixels.
/// </para>
/// </remarks>
internal sealed class PdfContentRewriter
{
    private const int MaxFormDepth = 12;

    private readonly PdfDocument _document;
    private readonly PdfContentRewriteOptions _options;
    private readonly Dictionary<PdfDictionary, PdfFontMetrics> _fonts = new(ReferenceEqualityComparer.Instance);

    private PdfContentRewriter(PdfDocument document, PdfContentRewriteOptions options)
    {
        _document = document;
        _options = options;
    }

    /// <summary>
    /// Gets how many glyphs were removed.
    /// </summary>
    public int GlyphsRemoved { get; private set; }

    /// <summary>
    /// Gets how many images were removed.
    /// </summary>
    public int ImagesRemoved { get; private set; }

    /// <summary>
    /// Gets how many form XObjects were rewritten.
    /// </summary>
    public int FormsRewritten { get; private set; }

    /// <summary>
    /// Gets how many marked-content sections were removed.
    /// </summary>
    public int SectionsRemoved { get; private set; }

    /// <summary>
    /// Rewrites one page.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="options">What to remove.</param>
    /// <returns>The rewriter, holding what was removed.</returns>
    public static PdfContentRewriter Rewrite(PdfPage page, PdfContentRewriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(options);

        var rewriter = new PdfContentRewriter(page.Owner, options);
        var sequence = ContentReader.ReadContent(page);
        var output = rewriter.Process(sequence, page.Resources, new GraphicsState(), 0);

        if (rewriter.GlyphsRemoved + rewriter.ImagesRemoved + rewriter.FormsRewritten + rewriter.SectionsRemoved > 0)
        {
            page.Contents.ReplaceContent(output);
        }

        return rewriter;
    }

    private CSequence Process(CSequence input, PdfDictionary resources, GraphicsState initial, int depth)
    {
        var output = new CSequence();
        var stack = new Stack<GraphicsState>();
        var state = initial.Clone();
        var text = PdfMatrix.Identity;
        var line = PdfMatrix.Identity;
        var fonts = PdfLowLevel.GetDictionary(resources, "/Font");
        var xobjects = PdfLowLevel.GetDictionary(resources, "/XObject");
        var properties = PdfLowLevel.GetDictionary(resources, "/Properties");
        var skipDepth = -1;
        var markedDepth = 0;

        state.Ctm = initial.Ctm;

        foreach (var item in input)
        {
            if (item is not COperator op)
            {
                if (skipDepth < 0)
                {
                    output.Add(item);
                }

                continue;
            }

            var operands = op.Operands;

            // Content inside a removed optional-content section is dropped whole, however it nests.
            if (op.Name is "BMC" or "BDC")
            {
                markedDepth++;

                if (skipDepth < 0 && op.Name == "BDC" && IsRemovedSection(operands, properties))
                {
                    skipDepth = markedDepth;
                    SectionsRemoved++;

                    continue;
                }
            }
            else if (op.Name == "EMC")
            {
                if (skipDepth == markedDepth)
                {
                    skipDepth = -1;
                    markedDepth--;

                    continue;
                }

                markedDepth--;
            }

            if (skipDepth >= 0)
            {
                continue;
            }

            switch (op.Name)
            {
                case "q":
                    stack.Push(state.Clone());

                    break;
                case "Q":
                    if (stack.Count > 0)
                    {
                        state = stack.Pop();
                    }

                    break;
                case "cm":
                    if (operands.Count >= 6)
                    {
                        state.Ctm = new PdfMatrix(Number(operands[0]), Number(operands[1]), Number(operands[2]), Number(operands[3]), Number(operands[4]), Number(operands[5])).Multiply(state.Ctm);
                    }

                    break;
                case "BT":
                    text = PdfMatrix.Identity;
                    line = PdfMatrix.Identity;

                    break;
                case "Tc":
                    state.CharacterSpacing = Number(operands, 0);

                    break;
                case "Tw":
                    state.WordSpacing = Number(operands, 0);

                    break;
                case "Tz":
                    state.HorizontalScaling = Number(operands, 0) / 100;

                    break;
                case "TL":
                    state.Leading = Number(operands, 0);

                    break;
                case "Ts":
                    state.Rise = Number(operands, 0);

                    break;
                case "Tr":
                    state.RenderingMode = (int)Number(operands, 0);

                    break;
                case "Tf":
                    if (operands.Count >= 2 && operands[0] is CName fontName)
                    {
                        state.Font = Metrics(PdfLowLevel.GetDictionary(fonts, fontName.Name));
                        state.FontSize = Number(operands[1]);
                    }

                    break;
                case "Td":
                    line = PdfMatrix.Translation(Number(operands, 0), Number(operands, 1)).Multiply(line);
                    text = line;

                    break;
                case "TD":
                    state.Leading = -Number(operands, 1);
                    line = PdfMatrix.Translation(Number(operands, 0), Number(operands, 1)).Multiply(line);
                    text = line;

                    break;
                case "Tm":
                    if (operands.Count >= 6)
                    {
                        line = new PdfMatrix(Number(operands[0]), Number(operands[1]), Number(operands[2]), Number(operands[3]), Number(operands[4]), Number(operands[5]));
                        text = line;
                    }

                    break;
                case "T*":
                    line = PdfMatrix.Translation(0, -state.Leading).Multiply(line);
                    text = line;

                    break;
                case "Tj" or "TJ" or "'" or "\"":
                    {
                        if (op.Name == "\"" && operands.Count >= 3)
                        {
                            state.WordSpacing = Number(operands[0]);
                            state.CharacterSpacing = Number(operands[1]);
                        }

                        if (op.Name is "'" or "\"")
                        {
                            line = PdfMatrix.Translation(0, -state.Leading).Multiply(line);
                            text = line;
                        }

                        var rewritten = ShowText(op, state, ref text);

                        if (rewritten is null)
                        {
                            output.Add(op);
                        }
                        else
                        {
                            foreach (var replacement in rewritten)
                            {
                                output.Add(replacement);
                            }
                        }

                        continue;
                    }

                case "Do":
                    if (operands.Count >= 1 && operands[0] is CName xobjectName)
                    {
                        var xobject = PdfLowLevel.GetDictionary(xobjects, xobjectName.Name);
                        var subtype = PdfLowLevel.Text(xobject?.Elements["/Subtype"]);

                        if (subtype == "Image" && IntersectsArea(state.Ctm.Transform(0, 0, 1, 1)))
                        {
                            ImagesRemoved++;

                            continue;
                        }

                        if (subtype == "Form" && depth < MaxFormDepth)
                        {
                            var replacement = RewriteForm(xobject, resources, state, depth);

                            if (replacement is not null)
                            {
                                output.Add(Operator("Do", new CName { Name = replacement }));

                                continue;
                            }
                        }
                    }

                    break;
                case "BI":
                    // An inline image carries its own data; it is removed when its unit square lands in an area.
                    if (IntersectsArea(state.Ctm.Transform(0, 0, 1, 1)))
                    {
                        ImagesRemoved++;

                        continue;
                    }

                    break;
            }

            output.Add(op);
        }

        return output;
    }

    /// <summary>
    /// Replays a text-showing operator, and returns the operators to write in its place when any of its
    /// glyphs are removed, or <see langword="null"/> when it is kept as it is.
    /// </summary>
    private List<COperator> ShowText(COperator op, GraphicsState state, ref PdfMatrix text)
    {
        var font = state.Font ?? PdfFontMetrics.From(null);
        var parts = new List<CObject>();

        if (op.Name == "TJ")
        {
            if (op.Operands.Count > 0 && op.Operands[0] is CArray array)
            {
                foreach (var element in array)
                {
                    parts.Add(element);
                }
            }
        }
        else if (op.Operands.Count > 0)
        {
            parts.Add(op.Operands[^1]);
        }

        var result = new CArray();
        var kept = new StringBuilder();
        var pendingAdvance = 0d;
        var removed = 0;
        var scale = state.FontSize * state.HorizontalScaling;
        var invisible = state.RenderingMode == 3 && _options.RemoveInvisibleText;
        CStringType stringType = CStringType.String;

        void FlushKept()
        {
            if (kept.Length > 0)
            {
                result.Add(new CString { Value = kept.ToString(), CStringType = stringType });
                kept.Clear();
            }
        }

        foreach (var part in parts)
        {
            if (part is CString value)
            {
                stringType = value.CStringType;

                var bytes = value.Value ?? string.Empty;
                var step = font.TwoByteCodes ? 2 : 1;

                for (var index = 0; index + step - 1 < bytes.Length; index += step)
                {
                    var code = font.TwoByteCodes ? (bytes[index] << 8) | bytes[index + 1] : bytes[index];
                    var width = font.Width(code);
                    var advance = ((width * state.FontSize) + state.CharacterSpacing + (!font.TwoByteCodes && code == 32 ? state.WordSpacing : 0)) * state.HorizontalScaling;
                    var remove = invisible;

                    if (!remove && _options.Areas.Count > 0)
                    {
                        var render = new PdfMatrix(scale, 0, 0, state.FontSize, 0, state.Rise).Multiply(text).Multiply(state.Ctm);
                        var box = render.Transform(0, font.Descent, width, font.Ascent);

                        remove = ContainsCenter(box);
                    }

                    if (remove)
                    {
                        FlushKept();
                        pendingAdvance += advance;
                        removed++;
                    }
                    else
                    {
                        if (pendingAdvance != 0)
                        {
                            result.Add(new CReal { Value = scale == 0 ? 0 : -pendingAdvance * 1000 / scale });
                            pendingAdvance = 0;
                        }

                        kept.Append(bytes, index, step);
                    }

                    text = PdfMatrix.Translation(advance, 0).Multiply(text);
                }
            }
            else
            {
                var adjustment = Number(part);
                var move = -adjustment / 1000 * scale;

                text = PdfMatrix.Translation(move, 0).Multiply(text);

                if (pendingAdvance != 0)
                {
                    pendingAdvance += move;
                }
                else
                {
                    FlushKept();
                    result.Add(part);
                }
            }
        }

        if (removed == 0)
        {
            return null;
        }

        FlushKept();

        // Movement left over after the last kept glyph still has to happen, or text that follows in the same
        // text object would shift left into the gap.
        if (pendingAdvance != 0)
        {
            result.Add(new CReal { Value = scale == 0 ? 0 : -pendingAdvance * 1000 / scale });
        }

        GlyphsRemoved += removed;

        var operators = new List<COperator>();

        if (op.Name == "\"")
        {
            operators.Add(Operator("Tw", new CReal { Value = state.WordSpacing }));
            operators.Add(Operator("Tc", new CReal { Value = state.CharacterSpacing }));
        }

        if (op.Name is "'" or "\"")
        {
            operators.Add(Operator("T*"));
        }

        if (result.Count > 0)
        {
            operators.Add(Operator("TJ", result));
        }

        return operators;
    }

    private string RewriteForm(PdfDictionary form, PdfDictionary parentResources, GraphicsState state, int depth)
    {
        var matrix = PdfLowLevel.GetArray(form, "/Matrix");
        var formMatrix = matrix is { Elements.Count: >= 6 }
            ? new PdfMatrix(
                PdfLowLevel.Number(matrix.Elements[0]),
                PdfLowLevel.Number(matrix.Elements[1]),
                PdfLowLevel.Number(matrix.Elements[2]),
                PdfLowLevel.Number(matrix.Elements[3]),
                PdfLowLevel.Number(matrix.Elements[4]),
                PdfLowLevel.Number(matrix.Elements[5]))
            : PdfMatrix.Identity;

        var ctm = formMatrix.Multiply(state.Ctm);

        // A form that cannot reach an area is left alone, unless something other than areas is being removed.
        if (!_options.RemoveInvisibleText && _options.RemoveOptionalContent is not { Count: > 0 })
        {
            var box = PdfLowLevel.GetRectangle(form, "/BBox");

            if (box is null || !IntersectsArea(ctm.Transform(box.X1, box.Y1, box.X2, box.Y2)))
            {
                return null;
            }
        }

        var bytes = form.Stream?.UnfilteredValue;

        if (bytes is null)
        {
            return null;
        }

        CSequence sequence;

        try
        {
            sequence = ContentReader.ReadContent(bytes);
        }
        catch (ContentReaderException)
        {
            return null;
        }

        var before = GlyphsRemoved + ImagesRemoved + SectionsRemoved + FormsRewritten;
        var formResources = PdfLowLevel.GetDictionary(form, "/Resources") ?? parentResources;
        var formState = state.Clone();

        formState.Ctm = ctm;

        var rewritten = Process(sequence, formResources, formState, depth + 1);

        if (GlyphsRemoved + ImagesRemoved + SectionsRemoved + FormsRewritten == before)
        {
            return null;
        }

        // The rewritten form is a private copy: the original may be drawn on other pages, where nothing is
        // to be removed from it.
        var copy = new PdfDictionary(_document);

        foreach (var key in form.Elements.Keys)
        {
            if (key is not "/Length" and not "/Filter" and not "/DecodeParms")
            {
                copy.Elements[key] = form.Elements[key];
            }
        }

        if (!copy.Elements.ContainsKey("/Resources") && parentResources is not null)
        {
            copy.Elements["/Resources"] = parentResources;
        }

        copy.CreateStream(rewritten.ToContent());
        _document.Internals.AddObject(copy);
        FormsRewritten++;

        var xobjects = PdfLowLevel.GetDictionary(parentResources, "/XObject");
        var index = 1;
        string name;

        do
        {
            name = "/Redacted" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            index++;
        }
        while (xobjects.Elements.ContainsKey(name));

        xobjects.Elements[name] = copy.Reference;

        return name;
    }

    private bool IsRemovedSection(CSequence operands, PdfDictionary properties)
    {
        if (_options.RemoveOptionalContent is not { Count: > 0 } || operands.Count < 2 ||
            operands[0] is not CName { Name: "/OC" } || operands[1] is not CName propertyName)
        {
            return false;
        }

        var group = PdfLowLevel.GetDictionary(properties, propertyName.Name);

        return group is not null && _options.RemoveOptionalContent.Contains(group);
    }

    private PdfFontMetrics Metrics(PdfDictionary font)
    {
        if (font is null)
        {
            return PdfFontMetrics.From(null);
        }

        if (!_fonts.TryGetValue(font, out var metrics))
        {
            _fonts[font] = metrics = PdfFontMetrics.From(font);
        }

        return metrics;
    }

    private bool ContainsCenter(PdfBox box)
    {
        var x = (box.Left + box.Right) / 2;
        var y = (box.Bottom + box.Top) / 2;

        foreach (var area in _options.Areas)
        {
            if (area.Contains(x, y))
            {
                return true;
            }
        }

        return false;
    }

    private bool IntersectsArea(PdfBox box)
    {
        foreach (var area in _options.Areas)
        {
            if (area.Intersects(box))
            {
                return true;
            }
        }

        return false;
    }

    private static COperator Operator(string name, params CObject[] operands)
    {
        var op = OpCodes.OperatorFromName(name);

        foreach (var operand in operands)
        {
            op.Operands.Add(operand);
        }

        return op;
    }

    private static double Number(CSequence operands, int index)
    {
        return index < operands.Count ? Number(operands[index]) : 0;
    }

    private static double Number(CObject value)
    {
        return value switch
        {
            CInteger integer => integer.Value,
            CReal real => real.Value,
            _ => 0,
        };
    }

    /// <summary>
    /// The part of the graphics state that decides where text lands.
    /// </summary>
    private sealed class GraphicsState
    {
        public PdfMatrix Ctm { get; set; } = PdfMatrix.Identity;

        public PdfFontMetrics Font { get; set; }

        public double FontSize { get; set; } = 1;

        public double CharacterSpacing { get; set; }

        public double WordSpacing { get; set; }

        public double HorizontalScaling { get; set; } = 1;

        public double Leading { get; set; }

        public double Rise { get; set; }

        public int RenderingMode { get; set; }

        public GraphicsState Clone()
        {
            return (GraphicsState)MemberwiseClone();
        }
    }
}
