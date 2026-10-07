using System.Text;

namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// A paragraph of slide text, with its inherited alignment, indentation and bullet resolved.
/// </summary>
public sealed class PresentationParagraph
{
    /// <summary>
    /// Gets or sets the outline level, from 0 (a top-level bullet) to 8.
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Gets or sets the horizontal alignment: <c>left</c>, <c>center</c>, <c>right</c> or <c>justify</c>.
    /// </summary>
    public string Alignment { get; set; } = "left";

    /// <summary>
    /// Gets or sets the marker drawn in front of the paragraph.
    /// </summary>
    public PresentationBullet Bullet { get; set; } = new();

    /// <summary>
    /// Gets or sets the runs of text that make up the paragraph.
    /// </summary>
    public IList<PresentationTextRun> Runs { get; set; } = [];

    /// <summary>
    /// Gets or sets the space above the paragraph, in points.
    /// </summary>
    public double SpaceBefore { get; set; }

    /// <summary>
    /// Gets or sets the space below the paragraph, in points.
    /// </summary>
    public double SpaceAfter { get; set; }

    /// <summary>
    /// Gets or sets the line spacing as a multiple of single spacing, where 1 is single spaced. Ignored when
    /// <see cref="LineSpacingPoints"/> is set.
    /// </summary>
    public double LineSpacing { get; set; } = 1;

    /// <summary>
    /// Gets or sets an exact line spacing in points, when the paragraph fixes one.
    /// </summary>
    public double? LineSpacingPoints { get; set; }

    /// <summary>
    /// Gets or sets the distance from the left edge of the text area to the text, in EMUs.
    /// </summary>
    public long MarginLeft { get; set; }

    /// <summary>
    /// Gets or sets how far the first line starts from <see cref="MarginLeft"/>, in EMUs. A bullet hangs
    /// with a negative indent, so the marker sits to the left of the text that follows it.
    /// </summary>
    public long Indent { get; set; }

    /// <summary>
    /// Gets or sets the size of the paragraph's end mark, in points, which sets the height of an empty
    /// paragraph.
    /// </summary>
    public double EndSize { get; set; } = 18;

    /// <summary>
    /// Gets the plain text of the paragraph, with soft line breaks written as new lines.
    /// </summary>
    public string Text
    {
        get
        {
            if (Runs.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();

            foreach (var run in Runs)
            {
                if (run.IsLineBreak)
                {
                    builder.Append('\n');
                    continue;
                }

                builder.Append(run.Text);
            }

            return builder.ToString();
        }
    }
}
