namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// A structural problem found by validating a package against the Office file format schemas.
/// </summary>
public sealed class PresentationValidationIssue
{
    /// <summary>
    /// Gets or sets the description of the problem.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the part of the package it is in, such as <c>/ppt/slides/slide3.xml</c>.
    /// </summary>
    public string Part { get; set; }

    /// <summary>
    /// Gets or sets the path to the offending element inside the part.
    /// </summary>
    public string Path { get; set; }

    /// <summary>
    /// Gets or sets the number of the slide the part belongs to, when it belongs to one.
    /// </summary>
    public int? SlideNumber { get; set; }
}
