using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using UglyToad.PdfPig.Content;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Lists, adds, changes and removes the comments and marks a reviewer puts on a PDF: sticky notes,
/// highlights, underlines, strikeouts, shapes, text boxes and stamps.
/// </summary>
/// <remarks>
/// Every annotation this class writes carries its own appearance, so it looks the same in every viewer
/// (and in the agent's preview) instead of depending on the viewer to draw it. Form-field widgets are left to
/// the form tools, and links to <c>add_pdf_links</c>.
/// </remarks>
internal static class PdfAnnotations
{
    private const double Kappa = 0.5523;

    private static readonly Dictionary<string, string> _subtypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["note"] = "Text",
        ["highlight"] = "Highlight",
        ["underline"] = "Underline",
        ["strikeout"] = "StrikeOut",
        ["squiggly"] = "Squiggly",
        ["rectangle"] = "Square",
        ["ellipse"] = "Circle",
        ["line"] = "Line",
        ["arrow"] = "Line",
        ["free_text"] = "FreeText",
        ["stamp"] = "Stamp",
    };

    private static readonly Dictionary<string, string> _names = new(StringComparer.Ordinal)
    {
        ["Text"] = "note",
        ["Highlight"] = "highlight",
        ["Underline"] = "underline",
        ["StrikeOut"] = "strikeout",
        ["Squiggly"] = "squiggly",
        ["Square"] = "rectangle",
        ["Circle"] = "ellipse",
        ["Line"] = "line",
        ["FreeText"] = "free_text",
        ["Stamp"] = "stamp",
        ["Ink"] = "ink",
        ["Link"] = "link",
        ["FileAttachment"] = "file_attachment",
        ["Polygon"] = "polygon",
        ["PolyLine"] = "polyline",
        ["Caret"] = "caret",
        ["Redact"] = "redact",
        ["Sound"] = "sound",
    };

    private static readonly string[] _standardStamps =
    [
        "Approved", "Experimental", "NotApproved", "AsIs", "Expired", "NotForPublicRelease", "Confidential",
        "Final", "Sold", "Departmental", "ForComment", "TopSecret", "Draft", "ForPublicRelease",
    ];

    /// <summary>
    /// Reads the tool's word for a kind of annotation, accepting the usual alternatives.
    /// </summary>
    /// <param name="type">The requested kind.</param>
    /// <returns>The kind, or the input lowercased when it is not one this class adds.</returns>
    public static string NormalizeType(string type)
    {
        var value = type?.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_') ?? string.Empty;

        return value switch
        {
            "comment" or "sticky_note" or "sticky" or "text" or "text_note" => "note",
            "highlighter" or "mark" => "highlight",
            "strike" or "strikethrough" or "strike_through" or "strike_out" or "cross_out" => "strikeout",
            "wavy" or "wavy_underline" or "squiggle" => "squiggly",
            "square" or "box" or "rect" => "rectangle",
            "circle" or "oval" => "ellipse",
            "text_box" or "textbox" or "freetext" or "typewriter" or "callout" => "free_text",
            _ => value,
        };
    }

    /// <summary>
    /// Lists the annotations of a document.
    /// </summary>
    /// <param name="bytes">The PDF.</param>
    /// <param name="password">The password, when the file is protected.</param>
    /// <param name="pages">The one-based pages to list.</param>
    /// <param name="types">The kinds to list, or an empty set for all.</param>
    /// <param name="widgets">Receives how many form-field widgets were left out.</param>
    /// <returns>The annotations, page by page.</returns>
    public static List<PdfAnnotationInfo> List(byte[] bytes, string password, IReadOnlyCollection<int> pages, ISet<string> types, out int widgets)
    {
        var result = new List<PdfAnnotationInfo>();
        widgets = 0;

        using var pig = PdfFiles.OpenForReading(bytes, password);
        using var document = PdfFiles.OpenForEditing(bytes, password);

        var ids = new Dictionary<PdfDictionary, string>();

        foreach (var (pageNumber, index, annotation) in Enumerate(document))
        {
            ids[annotation] = IdOf(annotation, pageNumber, index);
        }

        foreach (var (pageNumber, index, annotation) in Enumerate(document))
        {
            if (!pages.Contains(pageNumber))
            {
                continue;
            }

            var subtype = Subtype(annotation);

            if (subtype == "Widget")
            {
                widgets++;

                continue;
            }

            if (subtype == "Popup")
            {
                continue;
            }

            var parent = PdfLowLevel.GetDictionary(annotation, "/IRT");
            var type = parent is not null ? "reply" : _names.GetValueOrDefault(subtype, subtype.ToLowerInvariant());

            if (type == "line" && IsArrow(annotation))
            {
                type = "arrow";
            }

            if (types.Count > 0 && !types.Contains(type) && !(type == "reply" && types.Contains("note")))
            {
                continue;
            }

            var page = pig.GetPage(pageNumber);
            var rect = PdfLowLevel.GetRectangle(annotation);
            var box = rect is null ? default : new PdfBox(rect.X1, rect.Y1, rect.X2, rect.Y2);

            result.Add(new PdfAnnotationInfo
            {
                Id = ids[annotation],
                Page = pageNumber,
                Type = type,
                Position = box.ToTopLeft(page),
                Contents = PdfLowLevel.Text(annotation.Elements["/Contents"]),
                Author = PdfLowLevel.Text(annotation.Elements["/T"]),
                Subject = PdfLowLevel.Text(annotation.Elements["/Subj"]),
                Color = ReadColor(annotation, "/C")?.ToHex(),
                Modified = PdfLowLevel.Text(annotation.Elements["/M"]),
                MarkedText = subtype is "Highlight" or "Underline" or "StrikeOut" or "Squiggly" ? MarkedText(page, annotation) : null,
                Target = parent is not null && ids.TryGetValue(parent, out var parentId)
                    ? "reply to " + parentId
                    : subtype == "Link" ? LinkTarget(document, annotation) : null,
                Replies = ids.Keys.Count(candidate => ReferenceEquals(PdfLowLevel.GetDictionary(candidate, "/IRT"), annotation)),
            });
        }

        return result;
    }

    /// <summary>
    /// Adds annotations.
    /// </summary>
    /// <param name="bytes">The PDF.</param>
    /// <param name="password">The password, when the file is protected.</param>
    /// <param name="specs">The annotations to add.</param>
    /// <param name="now">The time the annotations are dated.</param>
    /// <param name="log">Receives a line per annotation added.</param>
    /// <param name="warnings">Receives the ones that could not be placed.</param>
    /// <returns>The edited PDF, or the input when nothing was added.</returns>
    public static byte[] Add(byte[] bytes, string password, IReadOnlyList<PdfAnnotationSpec> specs, DateTimeOffset now, List<string> log, List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(specs);

        var placements = Place(bytes, password, specs, warnings);

        if (placements.Count == 0)
        {
            return bytes;
        }

        using var document = PdfFiles.OpenForEditing(bytes, password);

        foreach (var placement in placements)
        {
            var spec = placement.Spec;
            var type = NormalizeType(spec.Type);
            var subtype = _subtypes[type];
            var page = document.Pages[placement.Page - 1];
            var annotation = new PdfDictionary(document);

            annotation.Elements.SetName("/Type", "/Annot");
            annotation.Elements.SetName("/Subtype", "/" + subtype);
            annotation.Elements["/NM"] = new PdfString("ca-" + Guid.NewGuid().ToString("N")[..12]);
            annotation.Elements["/CreationDate"] = new PdfString(FormatDate(now));
            annotation.Elements.SetInteger("/F", 4);
            annotation.Elements["/P"] = page.Reference;

            var color = PdfColor.Parse(spec.Color, DefaultColor(type, spec.Contents));

            SetColor(annotation, "/C", color);

            if (placement.Quads.Count > 0)
            {
                var quads = new List<double>();

                foreach (var quad in placement.Quads)
                {
                    quads.AddRange([quad.Left, quad.Top, quad.Right, quad.Top, quad.Left, quad.Bottom, quad.Right, quad.Bottom]);
                }

                annotation.Elements["/QuadPoints"] = PdfLowLevel.Numbers(document, [.. quads]);
            }

            switch (type)
            {
                case "note":
                    annotation.Elements.SetName("/Name", "/" + NoteIcon(spec.Icon));
                    annotation.Elements.SetBoolean("/Open", false);

                    break;

                case "line" or "arrow":
                    annotation.Elements["/L"] = PdfLowLevel.Numbers(document, placement.Line.X1, placement.Line.Y1, placement.Line.X2, placement.Line.Y2);

                    if (type == "arrow")
                    {
                        var ends = new PdfArray(document);
                        ends.Elements.Add(new PdfName("/None"));
                        ends.Elements.Add(new PdfName("/OpenArrow"));
                        annotation.Elements["/LE"] = ends;
                    }

                    break;

                case "stamp":
                    annotation.Elements.SetName("/Name", "/" + StampName(spec.Contents));

                    break;

                case "free_text":
                    annotation.Elements["/DA"] = new PdfString(string.Create(
                        CultureInfo.InvariantCulture,
                        $"/Helv {PdfLowLevel.Format(spec.FontSize ?? 11)} Tf {PdfLowLevel.Format(color.Red / 255d)} {PdfLowLevel.Format(color.Green / 255d)} {PdfLowLevel.Format(color.Blue / 255d)} rg"));

                    break;
            }

            annotation.Elements["/Rect"] = PdfLowLevel.Numbers(document, placement.Rect.Left, placement.Rect.Bottom, placement.Rect.Right, placement.Rect.Top);
            ApplyProperties(document, annotation, spec, type, now);
            document.Internals.AddObject(annotation);
            PdfLowLevel.GetAnnotations(page, create: true).Elements.Add(annotation.Reference);
            BuildAppearance(document, annotation);

            log.Add($"added {type} {PdfLowLevel.Text(annotation.Elements["/NM"])} on page {placement.Page}" + (placement.MarkedText is null ? string.Empty : $" over \"{placement.MarkedText}\""));
        }

        return PdfFiles.Save(document);
    }

    /// <summary>
    /// Changes annotations.
    /// </summary>
    /// <param name="bytes">The PDF.</param>
    /// <param name="password">The password, when the file is protected.</param>
    /// <param name="specs">The changes, each naming its annotation by <see cref="PdfAnnotationSpec.Id"/>.</param>
    /// <param name="now">The time the changes are dated.</param>
    /// <param name="log">Receives a line per annotation changed.</param>
    /// <returns>The edited PDF.</returns>
    public static byte[] Update(byte[] bytes, string password, IReadOnlyList<PdfAnnotationSpec> specs, DateTimeOffset now, List<string> log)
    {
        ArgumentNullException.ThrowIfNull(specs);

        using var pig = PdfFiles.OpenForReading(bytes, password);
        using var document = PdfFiles.OpenForEditing(bytes, password);

        var all = Enumerate(document).ToList();

        foreach (var spec in specs)
        {
            if (string.IsNullOrWhiteSpace(spec?.Id))
            {
                throw new PdfToolException("Each update needs the 'id' list returned.");
            }

            var found = all.FirstOrDefault(entry => string.Equals(IdOf(entry.Annotation, entry.Page, entry.Index), spec.Id.Trim(), StringComparison.OrdinalIgnoreCase));

            if (found.Annotation is null)
            {
                throw new PdfToolException($"There is no annotation \"{spec.Id}\". List them first; ids of unnamed annotations change when others on the page are removed.");
            }

            var annotation = found.Annotation;
            var subtype = Subtype(annotation);

            if (subtype is "Widget" or "Popup")
            {
                throw new PdfToolException($"\"{spec.Id}\" is a form field; change it with edit_pdf_form or fill_pdf_form.");
            }

            var type = _names.GetValueOrDefault(subtype, subtype.ToLowerInvariant());

            if (!string.IsNullOrEmpty(spec.Color) && PdfColor.TryParse(spec.Color, out var color))
            {
                SetColor(annotation, "/C", color);

                if (subtype == "FreeText")
                {
                    annotation.Elements["/DA"] = new PdfString(string.Create(
                        CultureInfo.InvariantCulture,
                        $"/Helv {PdfLowLevel.Format(spec.FontSize ?? FreeTextFontSize(annotation))} Tf {PdfLowLevel.Format(color.Red / 255d)} {PdfLowLevel.Format(color.Green / 255d)} {PdfLowLevel.Format(color.Blue / 255d)} rg"));
                }
            }
            else if (spec.FontSize is > 0 && subtype == "FreeText")
            {
                var current = ReadTextColor(annotation) ?? new PdfColor(0, 0, 0);

                annotation.Elements["/DA"] = new PdfString(string.Create(
                    CultureInfo.InvariantCulture,
                    $"/Helv {PdfLowLevel.Format(spec.FontSize.Value)} Tf {PdfLowLevel.Format(current.Red / 255d)} {PdfLowLevel.Format(current.Green / 255d)} {PdfLowLevel.Format(current.Blue / 255d)} rg"));
            }

            // A move keeps the size unless a new one is given; marks over text stay on their text.
            if ((spec.X.HasValue || spec.Y.HasValue || spec.Width.HasValue || spec.Height.HasValue) && subtype is not ("Highlight" or "Underline" or "StrikeOut" or "Squiggly"))
            {
                var page = pig.GetPage(found.Page);
                var rect = PdfLowLevel.GetRectangle(annotation);
                var (x, y, width, height) = new PdfBox(rect.X1, rect.Y1, rect.X2, rect.Y2).ToTopLeft(page);
                var moved = PdfBox.FromTopLeft(page, spec.X ?? x, spec.Y ?? y, spec.Width ?? width, spec.Height ?? height);

                if (subtype == "Line")
                {
                    var points = PdfLowLevel.Items(PdfLowLevel.GetArray(annotation, "/L")).Select(PdfLowLevel.Number).ToArray();

                    if (points.Length >= 4)
                    {
                        var dx = moved.Left - rect.X1;
                        var dy = moved.Top - rect.Y2;

                        annotation.Elements["/L"] = PdfLowLevel.Numbers(document, points[0] + dx, points[1] + dy, points[2] + dx, points[3] + dy);
                    }
                }

                annotation.Elements["/Rect"] = PdfLowLevel.Numbers(document, moved.Left, moved.Bottom, moved.Right, moved.Top);
            }

            ApplyProperties(document, annotation, spec, type, now);
            BuildAppearance(document, annotation);
            log.Add($"updated {type} {spec.Id} on page {found.Page}");
        }

        return PdfFiles.Save(document);
    }

    /// <summary>
    /// Removes annotations, with their pop-ups and replies.
    /// </summary>
    /// <param name="bytes">The PDF.</param>
    /// <param name="password">The password, when the file is protected.</param>
    /// <param name="ids">The ids to remove.</param>
    /// <param name="types">The kinds to remove from <paramref name="pages"/>, used when no ids are given.</param>
    /// <param name="pages">The one-based pages a removal by kind covers.</param>
    /// <param name="removed">Receives how many annotations were removed.</param>
    /// <returns>The edited PDF.</returns>
    public static byte[] Remove(byte[] bytes, string password, IReadOnlyCollection<string> ids, ISet<string> types, IReadOnlyCollection<int> pages, out int removed)
    {
        using var document = PdfFiles.OpenForEditing(bytes, password);

        var all = Enumerate(document).ToList();
        var targets = new HashSet<PdfDictionary>();
        var wanted = new HashSet<string>(ids.Select(id => id.Trim()), StringComparer.OrdinalIgnoreCase);

        foreach (var (pageNumber, index, annotation) in all)
        {
            var subtype = Subtype(annotation);

            if (subtype is "Widget" or "Popup")
            {
                continue;
            }

            if (wanted.Count > 0)
            {
                if (wanted.Remove(IdOf(annotation, pageNumber, index)))
                {
                    targets.Add(annotation);
                }

                continue;
            }

            var type = _names.GetValueOrDefault(subtype, subtype.ToLowerInvariant());

            if (pages.Contains(pageNumber) && (types.Count == 0 ? subtype != "Link" : types.Contains(type) || (type == "line" && types.Contains("arrow"))))
            {
                targets.Add(annotation);
            }
        }

        if (wanted.Count > 0)
        {
            throw new PdfToolException($"There is no annotation {string.Join(", ", wanted.Select(id => $"\"{id}\""))}. List them first.");
        }

        // Replies and pop-ups belong to what they answer, and go with it.
        bool grew;

        do
        {
            grew = false;

            foreach (var (_, _, annotation) in all)
            {
                if (!targets.Contains(annotation) &&
                    (PdfLowLevel.GetDictionary(annotation, "/IRT") is { } parent && targets.Contains(parent) ||
                    PdfLowLevel.GetDictionary(annotation, "/Parent") is { } owner && Subtype(annotation) == "Popup" && targets.Contains(owner)))
                {
                    grew |= targets.Add(annotation);
                }
            }
        }
        while (grew);

        removed = targets.Count(annotation => Subtype(annotation) != "Popup");

        for (var index = 0; index < document.PageCount; index++)
        {
            var list = PdfLowLevel.GetAnnotations(document.Pages[index], create: false);

            if (list is null)
            {
                continue;
            }

            for (var position = list.Elements.Count - 1; position >= 0; position--)
            {
                if (PdfLowLevel.Resolve(list.Elements[position]) is PdfDictionary annotation && targets.Contains(annotation))
                {
                    list.Elements.RemoveAt(position);
                }
            }
        }

        return removed == 0 ? bytes : PdfFiles.Save(document);
    }

    /// <summary>
    /// Draws an annotation's appearance from its own entries, replacing the one it had.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="annotation">The annotation.</param>
    public static void BuildAppearance(PdfDocument document, PdfDictionary annotation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotation);

        var rect = PdfLowLevel.GetRectangle(annotation);

        if (rect is null)
        {
            return;
        }

        var subtype = Subtype(annotation);
        var width = Math.Max(rect.Width, 1);
        var height = Math.Max(rect.Height, 1);
        var color = ReadColor(annotation, "/C") ?? new PdfColor(0, 0, 0);
        var opacity = annotation.Elements.ContainsKey("/CA") ? Math.Clamp(PdfLowLevel.Number(annotation.Elements["/CA"]), 0, 1) : 1;
        var border = BorderWidth(annotation);
        var content = new StringBuilder();
        var resources = new PdfDictionary(document);
        var state = new PdfDictionary(document);

        state.Elements.SetName("/Type", "/ExtGState");
        state.Elements.SetReal("/ca", opacity);
        state.Elements.SetReal("/CA", opacity);

        if (subtype == "Highlight")
        {
            state.Elements.SetName("/BM", "/Multiply");
        }

        var states = new PdfDictionary(document);
        states.Elements["/GS0"] = state;
        resources.Elements["/ExtGState"] = states;
        content.Append("/GS0 gs\n");

        string Point(double x, double y)
        {
            return PdfLowLevel.Format(x - rect.X1) + " " + PdfLowLevel.Format(y - rect.Y1);
        }

        switch (subtype)
        {
            case "Highlight":
                content.Append(Fill(color));

                foreach (var quad in Quads(annotation))
                {
                    content.Append(Point(quad.Left, quad.Top)).Append(" m ").Append(Point(quad.Right, quad.Top)).Append(" l ")
                        .Append(Point(quad.Right, quad.Bottom)).Append(" l ").Append(Point(quad.Left, quad.Bottom)).Append(" l h f\n");
                }

                break;

            case "Underline" or "StrikeOut" or "Squiggly":
                content.Append(Stroke(color));

                foreach (var quad in Quads(annotation))
                {
                    var lineWidth = Math.Max(0.6, quad.Height / 14);
                    content.Append(PdfLowLevel.Format(lineWidth)).Append(" w ");

                    if (subtype == "Squiggly")
                    {
                        var step = Math.Max(1.5, quad.Height / 6);
                        var baseline = quad.Bottom + (quad.Height * 0.06);
                        var up = true;

                        content.Append(Point(quad.Left, baseline)).Append(" m ");

                        for (var x = quad.Left + step; x <= quad.Right + 0.01; x += step, up = !up)
                        {
                            content.Append(Point(x, up ? baseline + step : baseline)).Append(" l ");
                        }

                        content.Append("S\n");
                    }
                    else
                    {
                        var y = subtype == "StrikeOut" ? quad.Bottom + (quad.Height * 0.42) : quad.Bottom + (quad.Height * 0.08);

                        content.Append(Point(quad.Left, y)).Append(" m ").Append(Point(quad.Right, y)).Append(" l S\n");
                    }
                }

                break;

            case "Square" or "Circle":
                {
                    var interior = ReadColor(annotation, "/IC");
                    var inset = border / 2;
                    var paint = interior is null ? "S" : "B";

                    content.Append(PdfLowLevel.Format(border)).Append(" w ").Append(Stroke(color));

                    if (interior is { } fill)
                    {
                        content.Append(Fill(fill));
                    }

                    if (subtype == "Square")
                    {
                        content.Append(PdfLowLevel.Format(inset)).Append(' ').Append(PdfLowLevel.Format(inset)).Append(' ')
                            .Append(PdfLowLevel.Format(width - border)).Append(' ').Append(PdfLowLevel.Format(height - border)).Append(" re ").Append(paint).Append('\n');
                    }
                    else
                    {
                        content.Append(Ellipse(width / 2, height / 2, (width / 2) - inset, (height / 2) - inset)).Append(paint).Append('\n');
                    }

                    break;
                }

            case "Line":
                {
                    var points = PdfLowLevel.Items(PdfLowLevel.GetArray(annotation, "/L")).Select(PdfLowLevel.Number).ToArray();

                    if (points.Length < 4)
                    {
                        return;
                    }

                    content.Append(PdfLowLevel.Format(border)).Append(" w 1 J ").Append(Stroke(color))
                        .Append(Point(points[0], points[1])).Append(" m ").Append(Point(points[2], points[3])).Append(" l S\n");

                    if (IsArrow(annotation))
                    {
                        var angle = Math.Atan2(points[3] - points[1], points[2] - points[0]);
                        var length = Math.Max(6, border * 5);

                        foreach (var side in new[] { 0.45, -0.45 })
                        {
                            content.Append(Point(points[2], points[3])).Append(" m ")
                                .Append(Point(points[2] - (length * Math.Cos(angle + side)), points[3] - (length * Math.Sin(angle + side)))).Append(" l S\n");
                        }
                    }

                    break;
                }

            case "FreeText":
                {
                    var fontSize = FreeTextFontSize(annotation);
                    var textColor = ReadTextColor(annotation) ?? new PdfColor(0, 0, 0);
                    var background = ReadColor(annotation, "/IC");

                    if (background is { } fill)
                    {
                        content.Append(Fill(fill)).Append("0 0 ").Append(PdfLowLevel.Format(width)).Append(' ').Append(PdfLowLevel.Format(height)).Append(" re f\n");
                    }

                    if (border > 0)
                    {
                        content.Append(PdfLowLevel.Format(border)).Append(" w ").Append(Stroke(color))
                            .Append(PdfLowLevel.Format(border / 2)).Append(' ').Append(PdfLowLevel.Format(border / 2)).Append(' ')
                            .Append(PdfLowLevel.Format(width - border)).Append(' ').Append(PdfLowLevel.Format(height - border)).Append(" re S\n");
                    }

                    var lines = Wrap(PdfLowLevel.Text(annotation.Elements["/Contents"]), fontSize, bold: false, width - 8 - (border * 2));
                    var y = height - border - 3 - (fontSize * 0.9);

                    content.Append("BT /Helv ").Append(PdfLowLevel.Format(fontSize)).Append(" Tf ").Append(Fill(textColor));

                    foreach (var line in lines)
                    {
                        if (y < border)
                        {
                            break;
                        }

                        content.Append("1 0 0 1 ").Append(PdfLowLevel.Format(4 + border)).Append(' ').Append(PdfLowLevel.Format(y)).Append(" Tm ")
                            .Append(PdfLowLevel.Literal(line)).Append(" Tj\n");
                        y -= fontSize * 1.2;
                    }

                    content.Append("ET\n");
                    AddFont(document, resources, "/Helv", "Helvetica");

                    break;
                }

            case "Stamp":
                {
                    var label = StampLabel(annotation);
                    var fontSize = Math.Min(height * 0.55, (width - 16) / Math.Max(Measure(label, 1, bold: true), 0.01));
                    var textWidth = Measure(label, fontSize, bold: true);

                    content.Append(Stroke(color)).Append(Fill(color))
                        .Append(PdfLowLevel.Format(Math.Max(1.5, height / 14))).Append(" w ")
                        .Append(RoundedRectangle(1.5, 1.5, width - 3, height - 3, Math.Min(6, height / 5))).Append("S\n")
                        .Append("BT /HelvB ").Append(PdfLowLevel.Format(fontSize)).Append(" Tf 1 0 0 1 ")
                        .Append(PdfLowLevel.Format((width - textWidth) / 2)).Append(' ').Append(PdfLowLevel.Format((height - (fontSize * 0.72)) / 2))
                        .Append(" Tm ").Append(PdfLowLevel.Literal(label)).Append(" Tj ET\n");

                    AddFont(document, resources, "/HelvB", "Helvetica-Bold");

                    break;
                }

            case "Text":
                // The note icon a viewer shows: a folded yellow page with lines of writing.
                content.Append(Fill(color)).Append("0.35 0.28 0 RG 0.8 w ")
                    .Append(RoundedRectangle(0.5, 0.5, width - 1, height - 1, 2)).Append("B\n")
                    .Append("0.35 0.28 0 RG 1 w ");

                for (var line = 0; line < 3; line++)
                {
                    var y = height * (0.7 - (line * 0.2));

                    content.Append(PdfLowLevel.Format(width * 0.2)).Append(' ').Append(PdfLowLevel.Format(y)).Append(" m ")
                        .Append(PdfLowLevel.Format(width * (line == 2 ? 0.55 : 0.8))).Append(' ').Append(PdfLowLevel.Format(y)).Append(" l S\n");
                }

                break;

            default:
                return;
        }

        var form = PdfLowLevel.CreateForm(document, width, height, content.ToString(), resources);
        var appearance = new PdfDictionary(document);

        appearance.Elements["/N"] = form.Reference;
        annotation.Elements["/AP"] = appearance;
    }

    private static List<Placement> Place(byte[] bytes, string password, IReadOnlyList<PdfAnnotationSpec> specs, List<string> warnings)
    {
        var placements = new List<Placement>();

        using var pdf = PdfFiles.OpenForReading(bytes, password);

        foreach (var spec in specs)
        {
            if (spec is null)
            {
                continue;
            }

            var type = NormalizeType(spec.Type);

            if (!_subtypes.ContainsKey(type))
            {
                throw new PdfToolException($"'{spec.Type}' is not an annotation this tool adds; use note, highlight, underline, strikeout, squiggly, rectangle, ellipse, line, arrow, free_text or stamp.");
            }

            var markup = type is "highlight" or "underline" or "strikeout" or "squiggly";

            if (!string.IsNullOrEmpty(spec.Text) && type is not ("line" or "arrow"))
            {
                List<int> pages = spec.Page is { } single ? [single] : PdfPageRange.Parse(spec.Pages, pdf.NumberOfPages);
                var count = 0;

                foreach (var number in pages)
                {
                    if (number < 1 || number > pdf.NumberOfPages)
                    {
                        throw new PdfToolException($"Page {number} does not exist; the document has {pdf.NumberOfPages} page(s).");
                    }

                    var page = pdf.GetPage(number);

                    foreach (var match in PdfTextFinder.Find(page, spec.Text, isRegex: false, spec.MatchCase == true, wholeWord: false, 500))
                    {
                        count++;

                        if (spec.Occurrence is { } occurrence && occurrence != count)
                        {
                            continue;
                        }

                        var union = match.Boxes.Aggregate((left, right) => left.Union(right));

                        placements.Add(markup
                            ? new Placement(spec, number, union.Inflate(1), [.. match.Boxes.Select(box => box.Inflate(0.5))], default, match.Text)
                            : new Placement(spec, number, AroundText(type, spec, union, page), [], default, match.Text));
                    }
                }

                if (count == 0 || (spec.Occurrence is { } wanted && wanted > count))
                {
                    warnings.Add($"\"{spec.Text}\" was not found" + (spec.Occurrence is null ? string.Empty : $" {spec.Occurrence} time(s)") + $"; no {type} was added for it.");
                }

                continue;
            }

            var pageNumber = spec.Page ?? 1;

            if (pageNumber < 1 || pageNumber > pdf.NumberOfPages)
            {
                throw new PdfToolException($"Page {pageNumber} does not exist; the document has {pdf.NumberOfPages} page(s).");
            }

            var target = pdf.GetPage(pageNumber);

            if (type is "line" or "arrow")
            {
                if (spec.X is null || spec.Y is null || spec.X2 is null || spec.Y2 is null)
                {
                    throw new PdfToolException($"A {type} needs 'x', 'y', 'x2' and 'y2' in points from the top-left.");
                }

                var visible = PdfBox.VisibleArea(target);
                var line = (X1: visible.Left + spec.X.Value, Y1: visible.Top - spec.Y.Value, X2: visible.Left + spec.X2.Value, Y2: visible.Top - spec.Y2.Value);
                var margin = Math.Max(4, (spec.BorderWidth ?? 1.5) * 6);
                var bounds = new PdfBox(Math.Min(line.X1, line.X2), Math.Min(line.Y1, line.Y2), Math.Max(line.X1, line.X2), Math.Max(line.Y1, line.Y2)).Inflate(margin);

                placements.Add(new Placement(spec, pageNumber, bounds, [], line, null));

                continue;
            }

            if (markup && (spec.Width is not > 0 || spec.Height is not > 0))
            {
                throw new PdfToolException($"A {type} needs the 'text' to mark, or an area ('x', 'y', 'width', 'height').");
            }

            var (defaultWidth, defaultHeight) = DefaultSize(type, spec);
            var area = PdfBox.FromTopLeft(target, spec.X ?? 72, spec.Y ?? 72, spec.Width ?? defaultWidth, spec.Height ?? defaultHeight);

            placements.Add(markup
                ? new Placement(spec, pageNumber, area, [area], default, null)
                : new Placement(spec, pageNumber, area, [], default, null));
        }

        return placements;
    }

    private static PdfBox AroundText(string type, PdfAnnotationSpec spec, PdfBox text, Page page)
    {
        var visible = PdfBox.VisibleArea(page);

        return type switch
        {
            "note" => NoteBeside(text, page, visible),
            "rectangle" or "ellipse" => text.Inflate(type == "ellipse" ? 6 : 3),
            _ => PlaceBeside(type, spec, text, visible),
        };
    }

    private static PdfBox NoteBeside(PdfBox text, Page page, PdfBox visible)
    {
        const double Size = 18;

        // A note goes after the end of the line it comments on, so it covers none of the words; when the line
        // runs to the edge, it goes in the right margin.
        var lineEnd = page.GetWords()
            .Select(word => PdfBox.From(word.BoundingBox))
            .Where(box => box.Bottom < text.Top && box.Top > text.Bottom)
            .Select(box => box.Right)
            .DefaultIfEmpty(text.Right)
            .Max();

        var left = lineEnd + 6 + Size <= visible.Right - 4
            ? lineEnd + 6
            : visible.Right - Size - 4;
        var middle = (text.Top + text.Bottom) / 2;

        return new PdfBox(left, middle - (Size / 2), left + Size, middle + (Size / 2));
    }

    private static PdfBox PlaceBeside(string type, PdfAnnotationSpec spec, PdfBox text, PdfBox visible)
    {
        var (width, height) = DefaultSize(type, spec);

        width = spec.Width ?? width;
        height = spec.Height ?? height;

        var left = Math.Min(text.Right + 6, visible.Right - width - 4);

        return new PdfBox(left, text.Top - height, left + width, text.Top);
    }

    private static (double Width, double Height) DefaultSize(string type, PdfAnnotationSpec spec)
    {
        switch (type)
        {
            case "note":
                return (18, 18);
            case "stamp":
                {
                    var label = (spec.Contents ?? "Approved").Trim().ToUpperInvariant();

                    return (Measure(label, 20, bold: true) + 28, 36);
                }

            case "free_text":
                {
                    var fontSize = spec.FontSize ?? 11;
                    var width = spec.Width ?? Math.Clamp(Measure(spec.Contents ?? string.Empty, fontSize, bold: false) + 14, 60, 260);
                    var lines = Wrap(spec.Contents ?? string.Empty, fontSize, bold: false, width - 8 - ((spec.BorderWidth ?? 1) * 2)).Count;

                    return (width, (Math.Max(lines, 1) * fontSize * 1.2) + 8);
                }

            default:
                return (120, 60);
        }
    }

    private static void ApplyProperties(PdfDocument document, PdfDictionary annotation, PdfAnnotationSpec spec, string type, DateTimeOffset now)
    {
        if (spec.Contents is not null)
        {
            annotation.Elements["/Contents"] = TextString(spec.Contents);
        }

        if (!string.IsNullOrEmpty(spec.Author))
        {
            annotation.Elements["/T"] = TextString(spec.Author);
        }

        if (!string.IsNullOrEmpty(spec.Subject))
        {
            annotation.Elements["/Subj"] = TextString(spec.Subject);
        }
        else if (!annotation.Elements.ContainsKey("/Subj"))
        {
            annotation.Elements["/Subj"] = TextString(type switch
            {
                "note" => "Comment",
                "free_text" => "Text Box",
                _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(type),
            });
        }

        if (spec.Opacity is { } opacity)
        {
            annotation.Elements.SetReal("/CA", Math.Clamp(opacity, 0.05, 1));
        }

        if (!string.IsNullOrEmpty(spec.FillColor) && PdfColor.TryParse(spec.FillColor, out var fill))
        {
            SetColor(annotation, "/IC", fill);
        }
        else if (string.Equals(spec.FillColor, "none", StringComparison.OrdinalIgnoreCase))
        {
            annotation.Elements.Remove("/IC");
        }

        if (spec.BorderWidth is { } width || (type is "rectangle" or "ellipse" or "line" or "arrow" or "free_text" && !annotation.Elements.ContainsKey("/BS")))
        {
            var style = new PdfDictionary(document);

            style.Elements.SetName("/Type", "/Border");
            style.Elements.SetReal("/W", Math.Clamp(spec.BorderWidth ?? (type == "free_text" ? 1 : 1.5), 0, 20));
            style.Elements.SetName("/S", "/S");
            annotation.Elements["/BS"] = style;
        }

        annotation.Elements["/M"] = new PdfString(FormatDate(now));
    }

    private static IEnumerable<(int Page, int Index, PdfDictionary Annotation)> Enumerate(PdfDocument document)
    {
        for (var pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
        {
            var list = PdfLowLevel.GetAnnotations(document.Pages[pageIndex], create: false);

            if (list is null)
            {
                continue;
            }

            for (var index = 0; index < list.Elements.Count; index++)
            {
                if (PdfLowLevel.Resolve(list.Elements[index]) is PdfDictionary annotation)
                {
                    yield return (pageIndex + 1, index + 1, annotation);
                }
            }
        }
    }

    private static string IdOf(PdfDictionary annotation, int page, int index)
    {
        var name = PdfLowLevel.Text(annotation.Elements["/NM"]);

        return string.IsNullOrWhiteSpace(name)
            ? string.Create(CultureInfo.InvariantCulture, $"p{page}-{index}")
            : name.Trim();
    }

    private static string Subtype(PdfDictionary annotation)
    {
        return annotation.Elements.GetName("/Subtype")?.TrimStart('/') ?? string.Empty;
    }

    private static bool IsArrow(PdfDictionary annotation)
    {
        return PdfLowLevel.Items(PdfLowLevel.GetArray(annotation, "/LE"))
            .Any(item => item is PdfName name && name.Value.Contains("Arrow", StringComparison.Ordinal));
    }

    private static List<PdfBox> Quads(PdfDictionary annotation)
    {
        var values = PdfLowLevel.Items(PdfLowLevel.GetArray(annotation, "/QuadPoints")).Select(PdfLowLevel.Number).ToArray();
        var quads = new List<PdfBox>();

        for (var index = 0; index + 7 < values.Length; index += 8)
        {
            var xs = new[] { values[index], values[index + 2], values[index + 4], values[index + 6] };
            var ys = new[] { values[index + 1], values[index + 3], values[index + 5], values[index + 7] };

            quads.Add(new PdfBox(xs.Min(), ys.Min(), xs.Max(), ys.Max()));
        }

        if (quads.Count == 0 && PdfLowLevel.GetRectangle(annotation) is { } rect)
        {
            quads.Add(new PdfBox(rect.X1, rect.Y1, rect.X2, rect.Y2));
        }

        return quads;
    }

    private static string MarkedText(Page page, PdfDictionary annotation)
    {
        var quads = Quads(annotation);
        var words = page.GetWords()
            .Where(word =>
            {
                var box = PdfBox.From(word.BoundingBox);
                var x = (box.Left + box.Right) / 2;
                var y = (box.Bottom + box.Top) / 2;

                return quads.Any(quad => quad.Contains(x, y));
            })
            .Select(word => word.Text);

        var text = string.Join(' ', words);

        return text.Length > 200 ? text[..199] + "…" : text;
    }

    private static string LinkTarget(PdfDocument document, PdfDictionary annotation)
    {
        var action = PdfLowLevel.GetDictionary(annotation, "/A");
        var uri = PdfLowLevel.Text(action?.Elements["/URI"]);

        if (!string.IsNullOrEmpty(uri))
        {
            return uri;
        }

        var destination = PdfLowLevel.Resolve(annotation.Elements["/Dest"] ?? action?.Elements["/D"]);

        if (destination is PdfArray array && array.Elements.Count > 0 && PdfLowLevel.Resolve(array.Elements[0]) is PdfDictionary target)
        {
            for (var index = 0; index < document.PageCount; index++)
            {
                if (ReferenceEquals(document.Pages[index], target))
                {
                    return "page " + (index + 1).ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        return destination is null ? null : "a named destination";
    }

    private static PdfColor? ReadColor(PdfDictionary annotation, string key)
    {
        var values = PdfLowLevel.Items(PdfLowLevel.GetArray(annotation, key)).Select(PdfLowLevel.Number).ToArray();

        static byte Channel(double value)
        {
            return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
        }

        return values.Length switch
        {
            1 => new PdfColor(Channel(values[0]), Channel(values[0]), Channel(values[0])),
            3 => new PdfColor(Channel(values[0]), Channel(values[1]), Channel(values[2])),
            4 => new PdfColor(
                Channel((1 - values[0]) * (1 - values[3])),
                Channel((1 - values[1]) * (1 - values[3])),
                Channel((1 - values[2]) * (1 - values[3]))),
            _ => null,
        };
    }

    private static PdfColor? ReadTextColor(PdfDictionary annotation)
    {
        var parts = (PdfLowLevel.Text(annotation.Elements["/DA"]) ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var index = Array.LastIndexOf(parts, "rg");

        if (index >= 3 &&
            double.TryParse(parts[index - 3], NumberStyles.Float, CultureInfo.InvariantCulture, out var red) &&
            double.TryParse(parts[index - 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var green) &&
            double.TryParse(parts[index - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var blue))
        {
            return new PdfColor((byte)Math.Round(red * 255), (byte)Math.Round(green * 255), (byte)Math.Round(blue * 255));
        }

        index = Array.LastIndexOf(parts, "g");

        if (index >= 1 && double.TryParse(parts[index - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var gray))
        {
            var channel = (byte)Math.Round(gray * 255);

            return new PdfColor(channel, channel, channel);
        }

        return null;
    }

    private static double FreeTextFontSize(PdfDictionary annotation)
    {
        var parts = (PdfLowLevel.Text(annotation.Elements["/DA"]) ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var index = Array.IndexOf(parts, "Tf");

        return index >= 1 && double.TryParse(parts[index - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && size > 0
            ? size
            : 11;
    }

    private static double BorderWidth(PdfDictionary annotation)
    {
        var style = PdfLowLevel.GetDictionary(annotation, "/BS");

        if (style is not null && style.Elements.ContainsKey("/W"))
        {
            return Math.Max(PdfLowLevel.Number(style.Elements["/W"]), 0);
        }

        var border = PdfLowLevel.Items(PdfLowLevel.GetArray(annotation, "/Border")).Select(PdfLowLevel.Number).ToArray();

        return border.Length >= 3 ? Math.Max(border[2], 0) : 1;
    }

    private static void SetColor(PdfDictionary annotation, string key, PdfColor color)
    {
        annotation.Elements[key] = PdfLowLevel.Numbers(annotation.Owner, color.Red / 255d, color.Green / 255d, color.Blue / 255d);
    }

    private static PdfColor DefaultColor(string type, string contents)
    {
        return type switch
        {
            "highlight" => new PdfColor(255, 229, 0),
            "underline" => new PdfColor(21, 101, 192),
            "note" => new PdfColor(255, 213, 79),
            "free_text" => new PdfColor(31, 41, 51),
            "stamp" => StampColor(contents),
            _ => new PdfColor(211, 47, 47),
        };
    }

    private static PdfColor StampColor(string label)
    {
        var text = (label ?? "Approved").Trim().ToUpperInvariant();

        if (text is "APPROVED" or "FINAL" or "COMPLETED" or "PAID" or "ACCEPTED" or "VERIFIED" or "RECEIVED")
        {
            return new PdfColor(46, 125, 50);
        }

        return text is "DRAFT" or "CONFIDENTIAL" or "REJECTED" or "VOID" or "NOT APPROVED" or "EXPIRED" or "TOP SECRET"
            ? new PdfColor(198, 40, 40)
            : new PdfColor(21, 101, 192);
    }

    private static string StampName(string label)
    {
        var compact = new string((label ?? "Approved").Where(char.IsLetterOrDigit).ToArray());
        var standard = _standardStamps.FirstOrDefault(name => string.Equals(name, compact, StringComparison.OrdinalIgnoreCase));

        return standard ?? (compact.Length == 0 ? "Approved" : compact);
    }

    private static string StampLabel(PdfDictionary annotation)
    {
        var contents = PdfLowLevel.Text(annotation.Elements["/Contents"]);

        if (!string.IsNullOrWhiteSpace(contents))
        {
            return contents.Trim().ToUpperInvariant();
        }

        var name = annotation.Elements.GetName("/Name")?.TrimStart('/') ?? "Approved";

        // NotApproved reads as NOT APPROVED.
        var spaced = new StringBuilder();

        foreach (var character in name)
        {
            if (char.IsUpper(character) && spaced.Length > 0)
            {
                spaced.Append(' ');
            }

            spaced.Append(char.ToUpperInvariant(character));
        }

        return spaced.ToString();
    }

    private static string NoteIcon(string icon)
    {
        var value = icon?.Trim() ?? string.Empty;

        return new[] { "Comment", "Note", "Help", "Key", "Insert", "Paragraph", "NewParagraph" }
            .FirstOrDefault(name => string.Equals(name, value.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase))
            ?? "Comment";
    }

    private static void AddFont(PdfDocument document, PdfDictionary resources, string name, string baseFont)
    {
        var font = new PdfDictionary(document);

        font.Elements.SetName("/Type", "/Font");
        font.Elements.SetName("/Subtype", "/Type1");
        font.Elements.SetName("/BaseFont", "/" + baseFont);
        font.Elements.SetName("/Encoding", "/WinAnsiEncoding");
        document.Internals.AddObject(font);

        var fonts = new PdfDictionary(document);
        fonts.Elements[name] = font.Reference;
        resources.Elements["/Font"] = fonts;
    }

    private static List<string> Wrap(string text, double fontSize, bool bold, double width)
    {
        var lines = new List<string>();

        foreach (var paragraph in (text ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var current = new StringBuilder();

            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;

                if (current.Length > 0 && Measure(candidate, fontSize, bold) > width)
                {
                    lines.Add(current.ToString());
                    current.Clear().Append(word);
                }
                else
                {
                    current.Clear().Append(candidate);
                }
            }

            lines.Add(current.ToString());
        }

        return lines;
    }

    private static double Measure(string text, double fontSize, bool bold)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        PdfFontConfiguration.Ensure();

        // Arial has Helvetica's widths, and is the metric-compatible font every host resolves.
        using var graphics = XGraphics.CreateMeasureContext(new XSize(2000, 2000), XGraphicsUnit.Point, XPageDirection.Downwards);

        return graphics.MeasureString(text, new XFont(PdfFontFamilies.Default, fontSize, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular)).Width;
    }

    private static PdfString TextString(string value)
    {
        return value.All(character => character < 128)
            ? new PdfString(value)
            : new PdfString(value, PdfStringEncoding.Unicode);
    }

    private static string FormatDate(DateTimeOffset value)
    {
        return "D:" + value.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "+00'00'";
    }

    private static string Fill(PdfColor color)
    {
        return PdfLowLevel.Format(color.Red / 255d) + " " + PdfLowLevel.Format(color.Green / 255d) + " " + PdfLowLevel.Format(color.Blue / 255d) + " rg ";
    }

    private static string Stroke(PdfColor color)
    {
        return PdfLowLevel.Format(color.Red / 255d) + " " + PdfLowLevel.Format(color.Green / 255d) + " " + PdfLowLevel.Format(color.Blue / 255d) + " RG ";
    }

    private static string Ellipse(double cx, double cy, double rx, double ry)
    {
        var ox = rx * Kappa;
        var oy = ry * Kappa;

        return string.Create(CultureInfo.InvariantCulture, $"{F(cx - rx)} {F(cy)} m {F(cx - rx)} {F(cy + oy)} {F(cx - ox)} {F(cy + ry)} {F(cx)} {F(cy + ry)} c ") +
            string.Create(CultureInfo.InvariantCulture, $"{F(cx + ox)} {F(cy + ry)} {F(cx + rx)} {F(cy + oy)} {F(cx + rx)} {F(cy)} c ") +
            string.Create(CultureInfo.InvariantCulture, $"{F(cx + rx)} {F(cy - oy)} {F(cx + ox)} {F(cy - ry)} {F(cx)} {F(cy - ry)} c ") +
            string.Create(CultureInfo.InvariantCulture, $"{F(cx - ox)} {F(cy - ry)} {F(cx - rx)} {F(cy - oy)} {F(cx - rx)} {F(cy)} c h ");
    }

    private static string RoundedRectangle(double x, double y, double width, double height, double radius)
    {
        var r = Math.Max(0, Math.Min(radius, Math.Min(width, height) / 2));
        var o = r * Kappa;
        var right = x + width;
        var top = y + height;

        return string.Create(CultureInfo.InvariantCulture, $"{F(x + r)} {F(y)} m {F(right - r)} {F(y)} l {F(right - r + o)} {F(y)} {F(right)} {F(y + r - o)} {F(right)} {F(y + r)} c ") +
            string.Create(CultureInfo.InvariantCulture, $"{F(right)} {F(top - r)} l {F(right)} {F(top - r + o)} {F(right - r + o)} {F(top)} {F(right - r)} {F(top)} c ") +
            string.Create(CultureInfo.InvariantCulture, $"{F(x + r)} {F(top)} l {F(x + r - o)} {F(top)} {F(x)} {F(top - r + o)} {F(x)} {F(top - r)} c ") +
            string.Create(CultureInfo.InvariantCulture, $"{F(x)} {F(y + r)} l {F(x)} {F(y + r - o)} {F(x + r - o)} {F(y)} {F(x + r)} {F(y)} c h ");
    }

    private static string F(double value)
    {
        return PdfLowLevel.Format(value);
    }

    private sealed record Placement(
        PdfAnnotationSpec Spec,
        int Page,
        PdfBox Rect,
        List<PdfBox> Quads,
        (double X1, double Y1, double X2, double Y2) Line,
        string MarkedText);
}
