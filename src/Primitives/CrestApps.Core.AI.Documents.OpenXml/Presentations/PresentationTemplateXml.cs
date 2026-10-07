using System.Globalization;
using System.Security;
using System.Text;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Writes the markup of the parts a new deck starts with: its theme, slide master, layouts and presentation
/// settings.
/// </summary>
/// <remarks>
/// These parts are the same for every deck apart from a handful of values — the colours, the fonts, the slide
/// size — so they are written as markup PowerPoint itself would produce rather than assembled element by
/// element. Placeholder positions are PowerPoint's own for a 16:9 slide, scaled to whatever size the deck is.
/// </remarks>
internal static class PresentationTemplateXml
{
    /// <summary>
    /// The namespace declarations every PresentationML part root carries.
    /// </summary>
    public const string Namespaces =
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
        "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"";

    /// <summary>
    /// The identifier of the first slide master; layouts are numbered after it.
    /// </summary>
    public const uint FirstMasterId = 2_147_483_648;

    private const long ReferenceWidth = 12_192_000;
    private const long ReferenceHeight = 6_858_000;

    /// <summary>
    /// The layouts a new deck carries, in the order PowerPoint lists them.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, string Type)> Layouts =
    [
        ("Title Slide", "title"),
        ("Title and Content", "obj"),
        ("Section Header", "secHead"),
        ("Two Content", "twoObj"),
        ("Comparison", "twoTxTwoObj"),
        ("Title Only", "titleOnly"),
        ("Blank", "blank"),
        ("Content with Caption", "objTx"),
        ("Picture with Caption", "picTx"),
    ];

    /// <summary>
    /// Writes a theme part.
    /// </summary>
    /// <param name="name">The theme's name.</param>
    /// <param name="colors">The twelve theme colours keyed by slot.</param>
    /// <param name="headingFont">The heading font.</param>
    /// <param name="bodyFont">The body font.</param>
    /// <returns>The markup.</returns>
    public static string Theme(string name, IReadOnlyDictionary<string, string> colors, string headingFont, string bodyFont)
    {
        var builder = new StringBuilder(6000);
        var safeName = Escape(name);

        builder.Append("<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"").Append(safeName).Append("\"><a:themeElements>");
        builder.Append("<a:clrScheme name=\"").Append(safeName).Append("\">");

        foreach (var slot in new[] { "dk1", "lt1", "dk2", "lt2", "accent1", "accent2", "accent3", "accent4", "accent5", "accent6", "hlink", "folHlink" })
        {
            builder.Append("<a:").Append(slot).Append("><a:srgbClr val=\"").Append(colors[slot]).Append("\"/></a:").Append(slot).Append('>');
        }

        builder.Append("</a:clrScheme>");
        builder.Append("<a:fontScheme name=\"").Append(safeName).Append("\">");
        builder.Append("<a:majorFont><a:latin typeface=\"").Append(Escape(headingFont)).Append("\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>");
        builder.Append("<a:minorFont><a:latin typeface=\"").Append(Escape(bodyFont)).Append("\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont>");
        builder.Append("</a:fontScheme>");
        builder.Append(FormatScheme);
        builder.Append("</a:themeElements><a:objectDefaults/><a:extraClrSchemeLst/></a:theme>");

        return builder.ToString();
    }

    /// <summary>
    /// Writes the presentation part.
    /// </summary>
    /// <param name="width">The slide width in EMUs.</param>
    /// <param name="height">The slide height in EMUs.</param>
    /// <param name="masterRelationshipId">The relationship identifier of the slide master.</param>
    /// <returns>The markup.</returns>
    public static string Presentation(long width, long height, string masterRelationshipId)
    {
        var type = (width, height) switch
        {
            (9_144_000, 6_858_000) => " type=\"screen4x3\"",
            (9_144_000, 5_715_000) => " type=\"screen16x10\"",
            _ => string.Empty,
        };

        return
            "<p:presentation " + Namespaces + " saveSubsetFonts=\"1\">" +
            "<p:sldMasterIdLst><p:sldMasterId id=\"" + FirstMasterId.ToString(CultureInfo.InvariantCulture) + "\" r:id=\"" + masterRelationshipId + "\"/></p:sldMasterIdLst>" +
            "<p:sldSz cx=\"" + Number(width) + "\" cy=\"" + Number(height) + "\"" + type + "/>" +
            "<p:notesSz cx=\"6858000\" cy=\"9144000\"/>" +
            "<p:defaultTextStyle>" + ParagraphLevels(true, 1800, "tx1", "+mn-lt", bullets: false, bodyIndents: false, algn: "l") + "</p:defaultTextStyle>" +
            "</p:presentation>";
    }

    /// <summary>
    /// Writes the presentation properties part.
    /// </summary>
    /// <returns>The markup.</returns>
    public static string PresentationProperties()
    {
        return "<p:presentationPr " + Namespaces + "/>";
    }

    /// <summary>
    /// Writes the view properties part.
    /// </summary>
    /// <returns>The markup.</returns>
    public static string ViewProperties()
    {
        return "<p:viewPr " + Namespaces + "><p:gridSpacing cx=\"76200\" cy=\"76200\"/></p:viewPr>";
    }

    /// <summary>
    /// Writes the table styles part.
    /// </summary>
    /// <returns>The markup.</returns>
    public static string TableStyles()
    {
        return "<a:tblStyleLst xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" def=\"{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}\"/>";
    }

    /// <summary>
    /// Writes the extended document properties part.
    /// </summary>
    /// <returns>The markup.</returns>
    public static string ApplicationProperties()
    {
        return "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\"><Application>Microsoft Office PowerPoint</Application></Properties>";
    }

    /// <summary>
    /// Writes the slide master part.
    /// </summary>
    /// <param name="width">The slide width in EMUs.</param>
    /// <param name="height">The slide height in EMUs.</param>
    /// <param name="layoutRelationshipIds">The relationship identifiers of the layouts, in order.</param>
    /// <param name="dark">Whether slides draw light text on a dark background.</param>
    /// <returns>The markup.</returns>
    public static string Master(long width, long height, IReadOnlyList<string> layoutRelationshipIds, bool dark)
    {
        var scale = new Scale(width, height);
        var builder = new StringBuilder(12000);

        builder.Append("<p:sldMaster ").Append(Namespaces).Append("><p:cSld>");
        builder.Append("<p:bg><p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef></p:bg>");
        builder.Append("<p:spTree>").Append(GroupHeader);

        builder.Append(MasterPlaceholder(2, "Title Placeholder 1", "type=\"title\"", scale.Rect(838200, 365125, 10515600, 1325563), "anchor=\"ctr\"", "<a:normAutofit/>", Paragraph("Click to edit Master title style")));
        builder.Append(MasterPlaceholder(3, "Text Placeholder 2", "type=\"body\" idx=\"1\"", scale.Rect(838200, 1825625, 10515600, 4351338), string.Empty, "<a:normAutofit/>",
            Paragraph("Click to edit Master text styles") +
            Paragraph("Second level", 1) +
            Paragraph("Third level", 2) +
            Paragraph("Fourth level", 3) +
            Paragraph("Fifth level", 4)));
        builder.Append(MasterPlaceholder(4, "Date Placeholder 3", "type=\"dt\" sz=\"half\" idx=\"2\"", scale.Rect(838200, 6356350, 2743200, 365125), "anchor=\"ctr\"", string.Empty, FooterParagraph("datetimeFigureOut", "1/1/2026"), footerStyle: "l"));
        builder.Append(MasterPlaceholder(5, "Footer Placeholder 4", "type=\"ftr\" sz=\"quarter\" idx=\"3\"", scale.Rect(4038600, 6356350, 4114800, 365125), "anchor=\"ctr\"", string.Empty, "<a:p><a:endParaRPr lang=\"en-US\"/></a:p>", footerStyle: "ctr"));
        builder.Append(MasterPlaceholder(6, "Slide Number Placeholder 5", "type=\"sldNum\" sz=\"quarter\" idx=\"4\"", scale.Rect(8610600, 6356350, 2743200, 365125), "anchor=\"ctr\"", string.Empty, FooterParagraph("slidenum", "‹#›"), footerStyle: "r"));

        builder.Append("</p:spTree></p:cSld>");
        builder.Append(dark
            ? "<p:clrMap bg1=\"dk1\" tx1=\"lt1\" bg2=\"dk2\" tx2=\"lt2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>"
            : "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>");

        builder.Append("<p:sldLayoutIdLst>");

        for (var index = 0; index < layoutRelationshipIds.Count; index++)
        {
            builder.Append("<p:sldLayoutId id=\"").Append((FirstMasterId + 1 + (uint)index).ToString(CultureInfo.InvariantCulture)).Append("\" r:id=\"").Append(layoutRelationshipIds[index]).Append("\"/>");
        }

        builder.Append("</p:sldLayoutIdLst>");
        builder.Append("<p:txStyles>");
        builder.Append("<p:titleStyle><a:lvl1pPr algn=\"l\" defTabSz=\"914400\" rtl=\"0\" eaLnBrk=\"1\" latinLnBrk=\"0\" hangingPunct=\"1\"><a:lnSpc><a:spcPct val=\"90000\"/></a:lnSpc><a:spcBef><a:spcPct val=\"0\"/></a:spcBef><a:buNone/><a:defRPr sz=\"4400\" kern=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"+mj-lt\"/><a:ea typeface=\"+mj-ea\"/><a:cs typeface=\"+mj-cs\"/></a:defRPr></a:lvl1pPr></p:titleStyle>");
        builder.Append("<p:bodyStyle>").Append(ParagraphLevels(false, 0, "tx1", "+mn-lt", bullets: true, bodyIndents: true, algn: "l")).Append("</p:bodyStyle>");
        builder.Append("<p:otherStyle>").Append(ParagraphLevels(true, 1800, "tx1", "+mn-lt", bullets: false, bodyIndents: false, algn: "l")).Append("</p:otherStyle>");
        builder.Append("</p:txStyles></p:sldMaster>");

        return builder.ToString();
    }

    /// <summary>
    /// Writes one of the layout parts listed in <see cref="Layouts"/>.
    /// </summary>
    /// <param name="index">The layout's position in <see cref="Layouts"/>.</param>
    /// <param name="width">The slide width in EMUs.</param>
    /// <param name="height">The slide height in EMUs.</param>
    /// <returns>The markup.</returns>
    public static string Layout(int index, long width, long height)
    {
        var (name, type) = Layouts[index];
        var scale = new Scale(width, height);
        var shapes = new StringBuilder();
        var id = 2;

        switch (type)
        {
            case "title":
                shapes.Append(LayoutPlaceholder(id++, "Title 1", "type=\"ctrTitle\"", scale.Rect(1524000, 1122363, 9144000, 2387600), "anchor=\"b\"",
                    "<a:lstStyle><a:lvl1pPr algn=\"ctr\"><a:defRPr sz=\"6000\"/></a:lvl1pPr></a:lstStyle>", Paragraph("Click to edit Master title style")));
                shapes.Append(LayoutPlaceholder(id++, "Subtitle 2", "type=\"subTitle\" idx=\"1\"", scale.Rect(1524000, 3602038, 9144000, 1655762), string.Empty,
                    "<a:lstStyle><a:lvl1pPr marL=\"0\" indent=\"0\" algn=\"ctr\"><a:buNone/><a:defRPr sz=\"2400\"/></a:lvl1pPr></a:lstStyle>", Paragraph("Click to edit Master subtitle style")));
                break;

            case "obj":
                shapes.Append(TitlePlaceholder(id++));
                shapes.Append(LayoutPlaceholder(id++, "Content Placeholder 2", "idx=\"1\"", null, null, null, BodyPrompt()));
                break;

            case "secHead":
                shapes.Append(LayoutPlaceholder(id++, "Title 1", "type=\"title\"", scale.Rect(831850, 1709738, 10515600, 2852737), "anchor=\"b\"",
                    "<a:lstStyle><a:lvl1pPr><a:defRPr sz=\"6000\"/></a:lvl1pPr></a:lstStyle>", Paragraph("Click to edit Master title style")));
                shapes.Append(LayoutPlaceholder(id++, "Text Placeholder 2", "type=\"body\" idx=\"1\"", scale.Rect(831850, 4589463, 10515600, 1500187), string.Empty,
                    "<a:lstStyle><a:lvl1pPr marL=\"0\" indent=\"0\"><a:buNone/><a:defRPr sz=\"2400\"><a:solidFill><a:schemeClr val=\"tx1\"><a:tint val=\"75000\"/></a:schemeClr></a:solidFill></a:defRPr></a:lvl1pPr></a:lstStyle>",
                    Paragraph("Click to edit Master text styles")));
                break;

            case "twoObj":
                shapes.Append(TitlePlaceholder(id++));
                shapes.Append(LayoutPlaceholder(id++, "Content Placeholder 2", "sz=\"half\" idx=\"1\"", scale.Rect(838200, 1825625, 5181600, 4351338), null, null, BodyPrompt()));
                shapes.Append(LayoutPlaceholder(id++, "Content Placeholder 3", "sz=\"half\" idx=\"2\"", scale.Rect(6172200, 1825625, 5181600, 4351338), null, null, BodyPrompt()));
                break;

            case "twoTxTwoObj":
                shapes.Append(LayoutPlaceholder(id++, "Title 1", "type=\"title\"", scale.Rect(839788, 365125, 10515600, 1325563), null, null, Paragraph("Click to edit Master title style")));
                shapes.Append(LayoutPlaceholder(id++, "Text Placeholder 2", "type=\"body\" idx=\"1\"", scale.Rect(839788, 1681163, 5157787, 823912), "anchor=\"b\"", HeadingListStyle, Paragraph("Click to edit Master text styles")));
                shapes.Append(LayoutPlaceholder(id++, "Content Placeholder 3", "sz=\"half\" idx=\"2\"", scale.Rect(839788, 2505075, 5157787, 3684588), null, null, BodyPrompt()));
                shapes.Append(LayoutPlaceholder(id++, "Text Placeholder 4", "type=\"body\" sz=\"quarter\" idx=\"3\"", scale.Rect(6172200, 1681163, 5183188, 823912), "anchor=\"b\"", HeadingListStyle, Paragraph("Click to edit Master text styles")));
                shapes.Append(LayoutPlaceholder(id++, "Content Placeholder 5", "sz=\"quarter\" idx=\"4\"", scale.Rect(6172200, 2505075, 5183188, 3684588), null, null, BodyPrompt()));
                break;

            case "titleOnly":
                shapes.Append(TitlePlaceholder(id++));
                break;

            case "objTx":
            case "picTx":
                shapes.Append(LayoutPlaceholder(id++, "Title 1", "type=\"title\"", scale.Rect(839788, 457200, 3932237, 1600200), "anchor=\"b\"",
                    "<a:lstStyle><a:lvl1pPr><a:defRPr sz=\"3200\"/></a:lvl1pPr></a:lstStyle>", Paragraph("Click to edit Master title style")));

                if (type == "objTx")
                {
                    shapes.Append(LayoutPlaceholder(id++, "Content Placeholder 2", "idx=\"1\"", scale.Rect(5183188, 987425, 6172200, 4873625), null, null, BodyPrompt()));
                }
                else
                {
                    shapes.Append(LayoutPlaceholder(id++, "Picture Placeholder 2", "type=\"pic\" idx=\"1\"", scale.Rect(5183188, 987425, 6172200, 4873625), null,
                        "<a:lstStyle><a:lvl1pPr marL=\"0\" indent=\"0\"><a:buNone/><a:defRPr sz=\"3200\"/></a:lvl1pPr></a:lstStyle>", "<a:p><a:endParaRPr lang=\"en-US\"/></a:p>"));
                }

                shapes.Append(LayoutPlaceholder(id++, "Text Placeholder 3", "type=\"body\" sz=\"half\" idx=\"2\"", scale.Rect(839788, 2057400, 3932237, 3811588), null,
                    "<a:lstStyle><a:lvl1pPr marL=\"0\" indent=\"0\"><a:buNone/><a:defRPr sz=\"1600\"/></a:lvl1pPr></a:lstStyle>", Paragraph("Click to edit Master text styles")));
                break;
        }

        shapes.Append(LayoutPlaceholder(id++, "Date Placeholder " + id.ToString(CultureInfo.InvariantCulture), "type=\"dt\" sz=\"half\" idx=\"10\"", null, null, null, FooterParagraph("datetimeFigureOut", "1/1/2026")));
        shapes.Append(LayoutPlaceholder(id++, "Footer Placeholder " + id.ToString(CultureInfo.InvariantCulture), "type=\"ftr\" sz=\"quarter\" idx=\"11\"", null, null, null, "<a:p><a:endParaRPr lang=\"en-US\"/></a:p>"));
        shapes.Append(LayoutPlaceholder(id, "Slide Number Placeholder " + id.ToString(CultureInfo.InvariantCulture), "type=\"sldNum\" sz=\"quarter\" idx=\"12\"", null, null, null, FooterParagraph("slidenum", "‹#›")));

        return
            "<p:sldLayout " + Namespaces + " type=\"" + type + "\" preserve=\"1\">" +
            "<p:cSld name=\"" + Escape(name) + "\"><p:spTree>" + GroupHeader + shapes + "</p:spTree></p:cSld>" +
            "<p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>";
    }

    /// <summary>
    /// Writes a notes master part.
    /// </summary>
    /// <returns>The markup.</returns>
    public static string NotesMaster()
    {
        return
            "<p:notesMaster " + Namespaces + "><p:cSld><p:bg><p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef></p:bg><p:spTree>" + GroupHeader +
            "<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Slide Image Placeholder 1\"/><p:cNvSpPr><a:spLocks noGrp=\"1\" noRot=\"1\" noChangeAspect=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"sldImg\" idx=\"2\"/></p:nvPr></p:nvSpPr>" +
            "<p:spPr><a:xfrm><a:off x=\"685800\" y=\"1143000\"/><a:ext cx=\"5486400\" cy=\"3086100\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:noFill/><a:ln w=\"12700\"><a:solidFill><a:prstClr val=\"black\"/></a:solidFill></a:ln></p:spPr></p:sp>" +
            "<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Notes Placeholder 2\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"body\" sz=\"quarter\" idx=\"3\"/></p:nvPr></p:nvSpPr>" +
            "<p:spPr><a:xfrm><a:off x=\"685800\" y=\"4400550\"/><a:ext cx=\"5486400\" cy=\"3600450\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr>" +
            "<p:txBody><a:bodyPr vert=\"horz\" lIns=\"91440\" tIns=\"45720\" rIns=\"91440\" bIns=\"45720\" rtlCol=\"0\"/><a:lstStyle/><a:p><a:pPr lvl=\"0\"/><a:r><a:rPr lang=\"en-US\"/><a:t>Click to edit Master text styles</a:t></a:r></a:p></p:txBody></p:sp>" +
            "</p:spTree></p:cSld>" +
            "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>" +
            "<p:notesStyle>" + ParagraphLevels(false, 1200, "tx1", "+mn-lt", bullets: false, bodyIndents: false, algn: "l") + "</p:notesStyle>" +
            "</p:notesMaster>";
    }

    /// <summary>
    /// Escapes text for an attribute or element value.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <returns>The escaped text.</returns>
    public static string Escape(string value)
    {
        return SecurityElement.Escape(value ?? string.Empty);
    }

    private const string GroupHeader =
        "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>" +
        "<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>";

    private const string HeadingListStyle =
        "<a:lstStyle><a:lvl1pPr marL=\"0\" indent=\"0\"><a:buNone/><a:defRPr sz=\"2400\" b=\"1\"/></a:lvl1pPr></a:lstStyle>";

    private const string FormatScheme =
        "<a:fmtScheme name=\"Office\"><a:fillStyleLst>" +
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
        "<a:gradFill rotWithShape=\"1\"><a:gsLst><a:gs pos=\"0\"><a:schemeClr val=\"phClr\"><a:lumMod val=\"110000\"/><a:satMod val=\"105000\"/><a:tint val=\"67000\"/></a:schemeClr></a:gs><a:gs pos=\"50000\"><a:schemeClr val=\"phClr\"><a:lumMod val=\"105000\"/><a:satMod val=\"103000\"/><a:tint val=\"73000\"/></a:schemeClr></a:gs><a:gs pos=\"100000\"><a:schemeClr val=\"phClr\"><a:lumMod val=\"105000\"/><a:satMod val=\"109000\"/><a:tint val=\"81000\"/></a:schemeClr></a:gs></a:gsLst><a:lin ang=\"5400000\" scaled=\"0\"/></a:gradFill>" +
        "<a:gradFill rotWithShape=\"1\"><a:gsLst><a:gs pos=\"0\"><a:schemeClr val=\"phClr\"><a:satMod val=\"103000\"/><a:lumMod val=\"102000\"/><a:tint val=\"94000\"/></a:schemeClr></a:gs><a:gs pos=\"50000\"><a:schemeClr val=\"phClr\"><a:satMod val=\"110000\"/><a:lumMod val=\"100000\"/><a:shade val=\"100000\"/></a:schemeClr></a:gs><a:gs pos=\"100000\"><a:schemeClr val=\"phClr\"><a:lumMod val=\"99000\"/><a:satMod val=\"120000\"/><a:shade val=\"78000\"/></a:schemeClr></a:gs></a:gsLst><a:lin ang=\"5400000\" scaled=\"0\"/></a:gradFill>" +
        "</a:fillStyleLst><a:lnStyleLst>" +
        "<a:ln w=\"6350\" cap=\"flat\" cmpd=\"sng\" algn=\"ctr\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:prstDash val=\"solid\"/><a:miter lim=\"800000\"/></a:ln>" +
        "<a:ln w=\"12700\" cap=\"flat\" cmpd=\"sng\" algn=\"ctr\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:prstDash val=\"solid\"/><a:miter lim=\"800000\"/></a:ln>" +
        "<a:ln w=\"19050\" cap=\"flat\" cmpd=\"sng\" algn=\"ctr\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:prstDash val=\"solid\"/><a:miter lim=\"800000\"/></a:ln>" +
        "</a:lnStyleLst><a:effectStyleLst>" +
        "<a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle>" +
        "<a:effectStyle><a:effectLst><a:outerShdw blurRad=\"57150\" dist=\"19050\" dir=\"5400000\" algn=\"ctr\" rotWithShape=\"0\"><a:srgbClr val=\"000000\"><a:alpha val=\"63000\"/></a:srgbClr></a:outerShdw></a:effectLst></a:effectStyle>" +
        "</a:effectStyleLst><a:bgFillStyleLst>" +
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
        "<a:solidFill><a:schemeClr val=\"phClr\"><a:tint val=\"95000\"/><a:satMod val=\"170000\"/></a:schemeClr></a:solidFill>" +
        "<a:gradFill rotWithShape=\"1\"><a:gsLst><a:gs pos=\"0\"><a:schemeClr val=\"phClr\"><a:tint val=\"93000\"/><a:satMod val=\"150000\"/><a:shade val=\"98000\"/><a:lumMod val=\"102000\"/></a:schemeClr></a:gs><a:gs pos=\"50000\"><a:schemeClr val=\"phClr\"><a:tint val=\"98000\"/><a:satMod val=\"130000\"/><a:shade val=\"90000\"/><a:lumMod val=\"103000\"/></a:schemeClr></a:gs><a:gs pos=\"100000\"><a:schemeClr val=\"phClr\"><a:shade val=\"63000\"/><a:satMod val=\"120000\"/></a:schemeClr></a:gs></a:gsLst><a:lin ang=\"5400000\" scaled=\"0\"/></a:gradFill>" +
        "</a:bgFillStyleLst></a:fmtScheme>";

    private static string TitlePlaceholder(int id)
    {
        return LayoutPlaceholder(id, "Title 1", "type=\"title\"", null, null, null, Paragraph("Click to edit Master title style"));
    }

    private static string BodyPrompt()
    {
        return Paragraph("Click to edit Master text styles") +
            Paragraph("Second level", 1) +
            Paragraph("Third level", 2) +
            Paragraph("Fourth level", 3) +
            Paragraph("Fifth level", 4);
    }

    private static string MasterPlaceholder(
        int id,
        string name,
        string placeholder,
        string transform,
        string bodyAttributes,
        string autoFit,
        string paragraphs,
        string footerStyle = null)
    {
        var listStyle = footerStyle is null
            ? "<a:lstStyle/>"
            : "<a:lstStyle><a:lvl1pPr algn=\"" + footerStyle + "\"><a:defRPr sz=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"><a:tint val=\"75000\"/></a:schemeClr></a:solidFill></a:defRPr></a:lvl1pPr></a:lstStyle>";

        return
            "<p:sp><p:nvSpPr><p:cNvPr id=\"" + id.ToString(CultureInfo.InvariantCulture) + "\" name=\"" + Escape(name) + "\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph " + placeholder + "/></p:nvPr></p:nvSpPr>" +
            "<p:spPr>" + transform + "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr>" +
            "<p:txBody><a:bodyPr vert=\"horz\" lIns=\"91440\" tIns=\"45720\" rIns=\"91440\" bIns=\"45720\" rtlCol=\"0\"" + (string.IsNullOrEmpty(bodyAttributes) ? string.Empty : " " + bodyAttributes) +
            (string.IsNullOrEmpty(autoFit) ? "/>" : ">" + autoFit + "</a:bodyPr>") +
            listStyle + paragraphs + "</p:txBody></p:sp>";
    }

    private static string LayoutPlaceholder(
        int id,
        string name,
        string placeholder,
        string transform,
        string bodyAttributes,
        string listStyle,
        string paragraphs)
    {
        return
            "<p:sp><p:nvSpPr><p:cNvPr id=\"" + id.ToString(CultureInfo.InvariantCulture) + "\" name=\"" + Escape(name) + "\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph " + placeholder + "/></p:nvPr></p:nvSpPr>" +
            "<p:spPr>" + (transform ?? string.Empty) + "</p:spPr>" +
            "<p:txBody>" + (string.IsNullOrEmpty(bodyAttributes) ? "<a:bodyPr/>" : "<a:bodyPr " + bodyAttributes + "/>") +
            (listStyle ?? "<a:lstStyle/>") + paragraphs + "</p:txBody></p:sp>";
    }

    private static string Paragraph(string text, int level = 0)
    {
        var properties = level == 0 ? "<a:pPr lvl=\"0\"/>" : "<a:pPr lvl=\"" + level.ToString(CultureInfo.InvariantCulture) + "\"/>";

        return "<a:p>" + properties + "<a:r><a:rPr lang=\"en-US\"/><a:t>" + Escape(text) + "</a:t></a:r></a:p>";
    }

    private static string FooterParagraph(string fieldType, string text)
    {
        var fieldId = fieldType == "slidenum" ? "{B6F15528-21DE-4FAA-801E-634DDDAF4B2B}" : "{C764DE79-268F-4C1A-8933-263129D2AF90}";

        return "<a:p><a:fld id=\"" + fieldId + "\" type=\"" + fieldType + "\"><a:rPr lang=\"en-US\"/><a:t>" + Escape(text) + "</a:t></a:fld><a:endParaRPr lang=\"en-US\"/></a:p>";
    }

    private static string ParagraphLevels(
        bool includeDefault,
        int size,
        string color,
        string font,
        bool bullets,
        bool bodyIndents,
        string algn)
    {
        var builder = new StringBuilder();

        if (includeDefault)
        {
            builder.Append("<a:defPPr><a:defRPr lang=\"en-US\"/></a:defPPr>");
        }

        int[] bodySizes = [2800, 2400, 2000, 1800, 1800, 1800, 1800, 1800, 1800];

        for (var level = 1; level <= 9; level++)
        {
            var levelSize = size > 0 ? size : bodySizes[level - 1];
            long margin = bodyIndents
                ? 228_600 + ((level - 1) * 457_200L)
                : (level - 1) * 457_200L;

            builder.Append("<a:lvl").Append(level).Append("pPr marL=\"").Append(Number(margin)).Append('"');

            if (bodyIndents)
            {
                builder.Append(" indent=\"-228600\"");
            }

            builder.Append(" algn=\"").Append(algn).Append("\" defTabSz=\"914400\" rtl=\"0\" eaLnBrk=\"1\" latinLnBrk=\"0\" hangingPunct=\"1\">");

            if (bullets)
            {
                builder.Append("<a:lnSpc><a:spcPct val=\"90000\"/></a:lnSpc><a:spcBef><a:spcPts val=\"").Append(level == 1 ? "1000" : "500").Append("\"/></a:spcBef><a:buFont typeface=\"Arial\" panose=\"020B0604020202020204\" pitchFamily=\"34\" charset=\"0\"/><a:buChar char=\"&#8226;\"/>");
            }

            builder.Append("<a:defRPr sz=\"").Append(levelSize.ToString(CultureInfo.InvariantCulture)).Append("\" kern=\"1200\"><a:solidFill><a:schemeClr val=\"").Append(color).Append("\"/></a:solidFill><a:latin typeface=\"").Append(font).Append("\"/><a:ea typeface=\"+mn-ea\"/><a:cs typeface=\"+mn-cs\"/></a:defRPr>");
            builder.Append("</a:lvl").Append(level).Append("pPr>");
        }

        return builder.ToString();
    }

    private static string Number(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private readonly struct Scale
    {
        private readonly double _x;
        private readonly double _y;

        public Scale(long width, long height)
        {
            _x = width / (double)ReferenceWidth;
            _y = height / (double)ReferenceHeight;
        }

        public string Rect(long x, long y, long width, long height)
        {
            return
                "<a:xfrm><a:off x=\"" + Number((long)Math.Round(x * _x)) + "\" y=\"" + Number((long)Math.Round(y * _y)) + "\"/>" +
                "<a:ext cx=\"" + Number((long)Math.Round(width * _x)) + "\" cy=\"" + Number((long)Math.Round(height * _y)) + "\"/></a:xfrm>";
        }
    }
}
