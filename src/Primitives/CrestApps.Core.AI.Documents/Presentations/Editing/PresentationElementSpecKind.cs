namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// The kind of element to insert.
/// </summary>
public enum PresentationElementSpecKind
{
    /// <summary>
    /// A text box, optionally with bullets.
    /// </summary>
    Text,

    /// <summary>
    /// A shape, optionally with text in it.
    /// </summary>
    Shape,

    /// <summary>
    /// A straight line or arrow.
    /// </summary>
    Line,

    /// <summary>
    /// A table.
    /// </summary>
    Table,

    /// <summary>
    /// A chart.
    /// </summary>
    Chart,

    /// <summary>
    /// A picture.
    /// </summary>
    Image,

    /// <summary>
    /// A simple icon drawn as a vector shape.
    /// </summary>
    Icon,

    /// <summary>
    /// A labelled link to a video.
    /// </summary>
    Video,

    /// <summary>
    /// A labelled link to an audio clip.
    /// </summary>
    Audio,

    /// <summary>
    /// A group of other elements.
    /// </summary>
    Group,

    /// <summary>
    /// A diagram — a process, cycle, timeline, hierarchy, pyramid, funnel, matrix, Venn diagram or card grid —
    /// laid out as a group of editable shapes.
    /// </summary>
    Diagram,
}
