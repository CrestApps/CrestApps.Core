namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// The house style recorded for a deck: how its titles, text, shapes, tables and charts should look.
/// </summary>
/// <remarks>
/// Recorded once and remembered for the rest of the conversation, the way the tabular agent remembers a
/// spreadsheet's formatting. A caller who said "navy titles in Georgia" three turns ago does not say it again
/// for every slide they add: new titles, text boxes, tables and charts pick the recorded style up, and a
/// follow-up only has to describe what changes.
/// </remarks>
public sealed class PresentationFormatting
{
    /// <summary>
    /// Gets or sets the style of slide titles.
    /// </summary>
    public PresentationTextStyle Title { get; set; }

    /// <summary>
    /// Gets or sets the style of subtitles.
    /// </summary>
    public PresentationTextStyle Subtitle { get; set; }

    /// <summary>
    /// Gets or sets the style of body placeholder text.
    /// </summary>
    public PresentationTextStyle Body { get; set; }

    /// <summary>
    /// Gets or sets the style of text in text boxes and shapes.
    /// </summary>
    public PresentationTextStyle Text { get; set; }

    /// <summary>
    /// Gets or sets how shapes are painted and outlined.
    /// </summary>
    public PresentationShapeStyle Shape { get; set; }

    /// <summary>
    /// Gets or sets how tables look.
    /// </summary>
    public PresentationTableStyle Table { get; set; }

    /// <summary>
    /// Gets or sets how charts look.
    /// </summary>
    public PresentationChartStyle Chart { get; set; }

    /// <summary>
    /// Gets or sets the background colour of new slides.
    /// </summary>
    public string BackgroundColor { get; set; }

    /// <summary>
    /// Gets or sets the gradient colours of the background of new slides.
    /// </summary>
    public IList<string> BackgroundGradient { get; set; }

    /// <summary>
    /// Gets or sets how many times the style has been changed, so a cached export made before a change is
    /// never handed back after it.
    /// </summary>
    public int Revision { get; set; }

    /// <summary>
    /// Gets a value indicating whether nothing has been recorded.
    /// </summary>
    public bool IsEmpty =>
        (Title?.IsEmpty ?? true) &&
        (Subtitle?.IsEmpty ?? true) &&
        (Body?.IsEmpty ?? true) &&
        (Text?.IsEmpty ?? true) &&
        (Shape?.IsEmpty ?? true) &&
        (Table?.IsEmpty ?? true) &&
        (Chart?.IsEmpty ?? true) &&
        BackgroundColor is null &&
        BackgroundGradient is not { Count: > 0 };

    /// <summary>
    /// Layers newly requested formatting on top of what was recorded, so a follow-up only changes what it
    /// names.
    /// </summary>
    /// <param name="overrides">The newly requested formatting.</param>
    /// <returns>A new specification holding the combined style, with the revision advanced.</returns>
    public PresentationFormatting Merge(PresentationFormatting overrides)
    {
        if (overrides is null)
        {
            return this;
        }

        return new PresentationFormatting
        {
            Title = PresentationTextStyle.Combine(Title, overrides.Title),
            Subtitle = PresentationTextStyle.Combine(Subtitle, overrides.Subtitle),
            Body = PresentationTextStyle.Combine(Body, overrides.Body),
            Text = PresentationTextStyle.Combine(Text, overrides.Text),
            Shape = PresentationShapeStyle.Combine(Shape, overrides.Shape),
            Table = PresentationTableStyle.Combine(Table, overrides.Table),
            Chart = PresentationChartStyle.Combine(Chart, overrides.Chart),
            BackgroundColor = overrides.BackgroundGradient is { Count: > 0 } ? overrides.BackgroundColor : overrides.BackgroundColor ?? BackgroundColor,
            BackgroundGradient = overrides.BackgroundColor is not null && overrides.BackgroundGradient is null
                ? null
                : overrides.BackgroundGradient ?? BackgroundGradient,
            Revision = Revision + 1,
        };
    }
}
