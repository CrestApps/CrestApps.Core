using System.Globalization;
using System.Security;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Formatting;

/// <summary>
/// Writes and removes a text watermark: a large, light text box behind the text, centered on every page through
/// the sections' headers, as Word places one.
/// </summary>
internal static class WordWatermark
{
    /// <summary>
    /// The name the watermark's drawing is given, so it can be found again.
    /// </summary>
    public const string Name = "CrestApps Watermark";

    /// <summary>
    /// Builds the paragraph that holds a watermark.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="color">The six-digit color.</param>
    /// <param name="size">The text size in points.</param>
    /// <param name="diagonal">Whether the text runs corner to corner.</param>
    /// <param name="id">The drawing id.</param>
    /// <returns>The paragraph.</returns>
    public static Paragraph Create(string text, string color, double size, bool diagonal, uint id)
    {
        var width = WordUnits.ToEmus(Math.Max(72, text.Length * size * 0.62));
        var height = WordUnits.ToEmus(size * 1.4);
        var rotation = diagonal ? " rot=\"18900000\"" : string.Empty;
        var xml = string.Create(CultureInfo.InvariantCulture, $"""
            <w:drawing xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:wps="http://schemas.microsoft.com/office/word/2010/wordprocessingShape"><wp:anchor distT="0" distB="0" distL="0" distR="0" simplePos="0" relativeHeight="251659264" behindDoc="1" locked="0" layoutInCell="1" allowOverlap="1"><wp:simplePos x="0" y="0"/><wp:positionH relativeFrom="page"><wp:align>center</wp:align></wp:positionH><wp:positionV relativeFrom="page"><wp:align>center</wp:align></wp:positionV><wp:extent cx="{width}" cy="{height}"/><wp:effectExtent l="0" t="0" r="0" b="0"/><wp:wrapNone/><wp:docPr id="{id}" name="{Name}" descr="Watermark: {SecurityElement.Escape(text)}"/><wp:cNvGraphicFramePr/><a:graphic><a:graphicData uri="http://schemas.microsoft.com/office/word/2010/wordprocessingShape"><wps:wsp><wps:cNvSpPr txBox="1"/><wps:spPr><a:xfrm{rotation}><a:off x="0" y="0"/><a:ext cx="{width}" cy="{height}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/><a:ln><a:noFill/></a:ln></wps:spPr><wps:txbx><w:txbxContent><w:p><w:pPr><w:jc w:val="center"/></w:pPr><w:r><w:rPr><w:b/><w:color w:val="{color}"/><w:sz w:val="{WordUnits.ToHalfPoints(size)}"/></w:rPr><w:t xml:space="preserve">{SecurityElement.Escape(text)}</w:t></w:r></w:p></w:txbxContent></wps:txbx><wps:bodyPr rot="0" vert="horz" wrap="none" lIns="0" tIns="0" rIns="0" bIns="0" anchor="ctr"><a:noAutofit/></wps:bodyPr></wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing>
            """);

        return new Paragraph(new Run(new Drawing(xml.Trim())));
    }

    /// <summary>
    /// Removes the watermark from every header of a document.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <returns>How many watermarks were removed.</returns>
    public static int Remove(WordPackage package)
    {
        var removed = 0;

        foreach (var header in package.MainPart.HeaderParts)
        {
            foreach (var drawing in header.Header?.Descendants<Drawing>().Where(IsWatermark).ToList() ?? [])
            {
                var paragraph = drawing.Ancestors<Paragraph>().FirstOrDefault();
                var run = drawing.Parent;

                drawing.Remove();

                if (run is Run && !run.ChildElements.Any(child => child is not RunProperties))
                {
                    run.Remove();
                }

                if (paragraph is not null && !paragraph.Descendants<Run>().Any() && paragraph.Parent?.Elements<Paragraph>().Count() > 1)
                {
                    paragraph.Remove();
                }

                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Puts a watermark into the default header of every section, creating empty headers where a section has none.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="build">Builds the watermark paragraph for a drawing id.</param>
    /// <returns>How many headers carry it.</returns>
    public static int Apply(WordPackage package, Func<uint, Paragraph> build)
    {
        Remove(package);

        var ids = WordDrawingIds.For(package);
        var done = new HashSet<OpenXmlPart>();

        var evenPages = package.MainPart.DocumentSettingsPart?.Settings?.GetFirstChild<EvenAndOddHeaders>() is not null;

        foreach (var section in WordSections.All(package))
        {
            // Every page of a section shows one of its headers: a different first page and even pages have
            // their own, and each needs the watermark.
            var types = new List<HeaderFooterValues> { HeaderFooterValues.Default };

            if (section.GetFirstChild<TitlePage>() is not null)
            {
                types.Add(HeaderFooterValues.First);
            }

            if (evenPages)
            {
                types.Add(HeaderFooterValues.Even);
            }

            foreach (var type in types)
            {
                var part = WordHeadersFooters.Find(package, section, header: true, type)
                    ?? WordHeadersFooters.Set(package, section, header: true, type, _ => []);

                if (!done.Add(part))
                {
                    continue;
                }

                var paragraph = build(ids.Next());
                var root = WordHeadersFooters.RootOf(part);

                package.Ids.Assign(paragraph);
                WordPackage.EnsureNamespaces(root);
                root.PrependChild(paragraph);
            }
        }

        return done.Count;
    }

    private static bool IsWatermark(Drawing drawing)
    {
        return string.Equals(drawing.Descendants<DW.DocProperties>().FirstOrDefault()?.Name?.Value, Name, StringComparison.Ordinal);
    }
}
