namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// The fixed identifiers PresentationML uses to say what a graphic frame holds and which extension a block of
/// markup belongs to.
/// </summary>
internal static class OpenXmlPresentationConstants
{
    /// <summary>
    /// The graphic data type of a table.
    /// </summary>
    public const string TableGraphicUri = "http://schemas.openxmlformats.org/drawingml/2006/table";

    /// <summary>
    /// The graphic data type of a chart.
    /// </summary>
    public const string ChartGraphicUri = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    /// <summary>
    /// The graphic data type of a SmartArt diagram.
    /// </summary>
    public const string DiagramGraphicUri = "http://schemas.openxmlformats.org/drawingml/2006/diagram";

    /// <summary>
    /// The graphic data type of an embedded or linked object.
    /// </summary>
    public const string OleGraphicUri = "http://schemas.openxmlformats.org/presentationml/2006/ole";

    /// <summary>
    /// The extension of <c>p:presentation</c> that holds its sections.
    /// </summary>
    public const string SectionListExtensionUri = "{521415D9-36F7-43E2-AB2F-B90AF26B5E84}";

    /// <summary>
    /// The extension of a shape's non-visual properties that marks it decorative.
    /// </summary>
    public const string DecorativeExtensionUri = "{C183D7F6-B498-43B3-948B-1728B52AA6E4}";

    /// <summary>
    /// The namespace of the decorative marker.
    /// </summary>
    public const string DecorativeNamespace = "http://schemas.microsoft.com/office/drawing/2017/decorative";

    /// <summary>
    /// The namespace of PowerPoint 2010 extensions, including sections.
    /// </summary>
    public const string PowerPoint2010Namespace = "http://schemas.microsoft.com/office/powerpoint/2010/main";

    /// <summary>
    /// The namespace of relationships.
    /// </summary>
    public const string RelationshipsNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>
    /// The content type of an embedded workbook.
    /// </summary>
    public const string WorkbookContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// The action that makes a link jump to another slide.
    /// </summary>
    public const string SlideJumpAction = "ppaction://hlinksldjump";

    /// <summary>
    /// The prefix of the actions that jump relative to the current slide.
    /// </summary>
    public const string ShowJumpActionPrefix = "ppaction://hlinkshowjump?jump=";
}
