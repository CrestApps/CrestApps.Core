using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// Creates, reads, edits and validates presentation packages for the presentation agent.
/// </summary>
/// <remarks>
/// The presentation workspace stores each deck as the package itself, so an uploaded deck keeps every master,
/// layout, animation and piece of content the tools know nothing about, and exporting it is handing that
/// package back. The engine is the only thing that understands the package format; the tools speak to it in
/// <see cref="PresentationEdit"/> terms and read it back as a <see cref="PresentationModel"/>.
/// </remarks>
public interface IPresentationEngine
{
    /// <summary>
    /// Creates a new deck.
    /// </summary>
    /// <param name="options">How to set the deck up.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new package.</returns>
    Task<byte[]> CreateAsync(PresentationCreateOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a deck into its resolved model.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="options">How much to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The model.</returns>
    Task<PresentationModel> ReadAsync(byte[] package, PresentationReadOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a batch of edits. Either every edit is applied or, when one fails, none is: the package passed
    /// in is never modified and no partial result is returned.
    /// </summary>
    /// <param name="package">The package to edit.</param>
    /// <param name="edits">The edits, applied in order.</param>
    /// <param name="context">What the engine needs to know about the conversation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The edited package and a description of what changed.</returns>
    /// <exception cref="PresentationEditException">An edit could not be applied.</exception>
    Task<PresentationEditResult> EditAsync(
        byte[] package,
        IReadOnlyList<PresentationEdit> edits,
        PresentationEditContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a package against the Office file format schemas.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The problems found; empty when the package is valid.</returns>
    Task<IReadOnlyList<PresentationValidationIssue>> ValidateAsync(byte[] package, CancellationToken cancellationToken = default);
}
