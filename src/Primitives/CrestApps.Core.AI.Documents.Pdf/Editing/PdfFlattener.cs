using System.Text;
using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Turns form fields and annotations into ordinary page content: each one's appearance is painted onto the
/// page where it sat, and the interactive object is removed.
/// </summary>
/// <remarks>
/// A flattened form prints and displays the same everywhere and can no longer be changed, which is what a
/// submitted form or a reviewed document is asked to become. A field with a value but no appearance of its
/// own — one filled by software that relies on the viewer to draw it — is given one first, so flattening
/// never loses a value.
/// </remarks>
internal static class PdfFlattener
{
    private const int HiddenFlag = 2;
    private const int NoViewFlag = 32;

    /// <summary>
    /// Flattens a document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="forms">Whether form fields are flattened.</param>
    /// <param name="annotations">Whether other annotations (comments, highlights, stamps, shapes) are flattened.</param>
    /// <param name="pages">The one-based pages to flatten, or <see langword="null"/> for all.</param>
    /// <returns>How many fields and annotations were flattened.</returns>
    public static (int Fields, int Annotations) Flatten(PdfDocument document, bool forms, bool annotations, IReadOnlyCollection<int> pages = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (forms)
        {
            // Values set without an appearance are drawn first, so the painted result shows them.
            foreach (var field in PdfFormFields.Read(document))
            {
                if (field.Type is "text" or "dropdown" or "listbox" && !string.IsNullOrEmpty(field.Value) && field.Widgets.Any(widget => PdfLowLevel.GetDictionary(widget, "/AP") is null))
                {
                    PdfFormFields.DrawTextAppearance(document, field, field.Value);
                }
            }
        }

        var fieldCount = 0;
        var annotationCount = 0;

        for (var index = 0; index < document.PageCount; index++)
        {
            if (pages is not null && !pages.Contains(index + 1))
            {
                continue;
            }

            var page = document.Pages[index];
            var list = PdfLowLevel.GetAnnotations(page, create: false);

            if (list is null)
            {
                continue;
            }

            var drawing = new StringBuilder();
            var removed = new List<int>();

            for (var position = 0; position < list.Elements.Count; position++)
            {
                if (PdfLowLevel.Resolve(list.Elements[position]) is not PdfDictionary annotation)
                {
                    continue;
                }

                var subtype = PdfLowLevel.Text(annotation.Elements["/Subtype"]);
                var isWidget = subtype == "Widget";

                if (subtype is "Link" or "Popup" || (isWidget && !forms) || (!isWidget && !annotations))
                {
                    continue;
                }

                var flags = (int)PdfLowLevel.Number(annotation.Elements["/F"]);

                if ((flags & (HiddenFlag | NoViewFlag)) == 0 && Appearance(annotation) is { } appearance)
                {
                    drawing.Append(Paint(page, annotation, appearance));
                }

                removed.Add(position);

                if (isWidget)
                {
                    fieldCount++;
                }
                else
                {
                    annotationCount++;
                }
            }

            if (annotations)
            {
                // A popup belongs to a comment; with the comment flattened it has nothing left to pop up from.
                for (var position = 0; position < list.Elements.Count; position++)
                {
                    if (PdfLowLevel.Resolve(list.Elements[position]) is PdfDictionary popup &&
                        PdfLowLevel.Text(popup.Elements["/Subtype"]) == "Popup" &&
                        !removed.Contains(position))
                    {
                        removed.Add(position);
                    }
                }
            }

            foreach (var position in removed.OrderDescending())
            {
                list.Elements.RemoveAt(position);
            }

            if (drawing.Length > 0)
            {
                PdfLowLevel.AppendIsolated(page, drawing.ToString());
            }
        }

        if (forms && pages is null)
        {
            // With every widget painted into the pages, the form has nothing left to hold.
            document.Internals.Catalog.Elements.Remove("/AcroForm");
        }

        return (fieldCount, annotationCount);
    }

    private static PdfDictionary Appearance(PdfDictionary annotation)
    {
        var normal = PdfLowLevel.Resolve(PdfLowLevel.GetDictionary(annotation, "/AP")?.Elements["/N"]) as PdfDictionary;

        if (normal is null)
        {
            return null;
        }

        if (normal.Stream is not null)
        {
            return normal;
        }

        // An appearance with states (a checkbox's on and off) is drawn in the state the widget is in.
        var state = PdfLowLevel.Text(annotation.Elements["/AS"]);

        return state is null
            ? null
            : PdfLowLevel.Resolve(normal.Elements["/" + state]) as PdfDictionary;
    }

    private static string Paint(PdfPage page, PdfDictionary annotation, PdfDictionary appearance)
    {
        var rectangle = PdfLowLevel.GetRectangle(annotation);
        var box = PdfLowLevel.GetArray(appearance, "/BBox");

        if (rectangle is null || box is null || box.Elements.Count < 4)
        {
            return string.Empty;
        }

        if (!appearance.Elements.ContainsKey("/Subtype"))
        {
            appearance.Elements.SetName("/Type", "/XObject");
            appearance.Elements.SetName("/Subtype", "/Form");
        }

        var (left, bottom, right, top) = TransformedBox(appearance, box);
        var width = right - left;
        var height = top - bottom;

        if (width <= 0 || height <= 0)
        {
            return string.Empty;
        }

        // The appearance's own matrix is applied by the renderer when the form is drawn; this maps the box
        // that matrix produces onto the annotation's rectangle, as the specification lays out.
        var scaleX = rectangle.Width / width;
        var scaleY = rectangle.Height / height;
        var translateX = rectangle.X1 - (left * scaleX);
        var translateY = rectangle.Y1 - (bottom * scaleY);
        var name = PdfLowLevel.AddXObject(page, appearance, "Flat");

        return "q " + PdfLowLevel.Format(scaleX) + " 0 0 " + PdfLowLevel.Format(scaleY) + " " +
            PdfLowLevel.Format(translateX) + " " + PdfLowLevel.Format(translateY) + " cm " + name + " Do Q\n";
    }

    private static (double Left, double Bottom, double Right, double Top) TransformedBox(PdfDictionary appearance, PdfArray box)
    {
        var x1 = PdfLowLevel.Number(box.Elements[0]);
        var y1 = PdfLowLevel.Number(box.Elements[1]);
        var x2 = PdfLowLevel.Number(box.Elements[2]);
        var y2 = PdfLowLevel.Number(box.Elements[3]);
        var matrix = PdfLowLevel.GetArray(appearance, "/Matrix");

        if (matrix is null || matrix.Elements.Count < 6)
        {
            return (Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
        }

        var a = PdfLowLevel.Number(matrix.Elements[0]);
        var b = PdfLowLevel.Number(matrix.Elements[1]);
        var c = PdfLowLevel.Number(matrix.Elements[2]);
        var d = PdfLowLevel.Number(matrix.Elements[3]);
        var e = PdfLowLevel.Number(matrix.Elements[4]);
        var f = PdfLowLevel.Number(matrix.Elements[5]);

        var xs = new List<double>(4);
        var ys = new List<double>(4);

        foreach (var (x, y) in new[] { (x1, y1), (x2, y1), (x1, y2), (x2, y2) })
        {
            xs.Add((a * x) + (c * y) + e);
            ys.Add((b * x) + (d * y) + f);
        }

        return (xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }
}
