namespace CrestApps.Core.AI.Models;

/// <summary>
/// The detail an article carries beyond the fields every knowledge object has.
/// </summary>
public sealed class ArticleDetails
{
    /// <summary>
    /// Gets or sets the authors credited with the article.
    /// </summary>
    public IList<string> Authors { get; set; } = [];

    /// <summary>
    /// Gets or sets the running-head label that says what kind of article this is.
    /// </summary>
    public string SectionLabel { get; set; }

    /// <summary>
    /// Gets or sets the abstract, or the opening paragraph when none is printed.
    /// </summary>
    public string Abstract { get; set; }
}
