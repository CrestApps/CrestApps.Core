namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// One change to a deck, expressed in the format-neutral terms the tools speak, for an
/// <see cref="IPresentationEngine"/> to apply to the package.
/// </summary>
/// <remarks>
/// The tools describe what the reader asked for; the engine knows how a presentation package records it.
/// Keeping the two apart means the tools never touch PresentationML, the engine never parses a model's
/// arguments, and a batch of edits is applied as a unit: all of it, or — when one step cannot be done —
/// none of it.
/// </remarks>
public abstract class PresentationEdit
{
}
