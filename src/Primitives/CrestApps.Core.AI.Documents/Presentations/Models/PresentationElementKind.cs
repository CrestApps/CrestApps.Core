namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// What kind of thing a slide element is.
/// </summary>
public enum PresentationElementKind
{
    /// <summary>
    /// A shape the author drew, with or without text in it.
    /// </summary>
    Shape,

    /// <summary>
    /// A text box: a shape that exists to hold text and has no outline or fill of its own by default.
    /// </summary>
    TextBox,

    /// <summary>
    /// A placeholder inherited from the slide's layout, such as the title or the body.
    /// </summary>
    Placeholder,

    /// <summary>
    /// A picture.
    /// </summary>
    Picture,

    /// <summary>
    /// A table.
    /// </summary>
    Table,

    /// <summary>
    /// A chart.
    /// </summary>
    Chart,

    /// <summary>
    /// A group of other elements that move and resize together.
    /// </summary>
    Group,

    /// <summary>
    /// A line or connector.
    /// </summary>
    Connector,

    /// <summary>
    /// An embedded or linked video.
    /// </summary>
    Video,

    /// <summary>
    /// An embedded or linked audio clip.
    /// </summary>
    Audio,

    /// <summary>
    /// A SmartArt graphic.
    /// </summary>
    Diagram,

    /// <summary>
    /// An embedded or linked object, such as a worksheet or a document.
    /// </summary>
    EmbeddedObject,

    /// <summary>
    /// A 3D model.
    /// </summary>
    Model3D,

    /// <summary>
    /// Digital ink.
    /// </summary>
    Ink,

    /// <summary>
    /// Anything else the reader does not recognise.
    /// </summary>
    Unknown,
}
