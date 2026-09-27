namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// A problem found in a deck.
/// </summary>
/// <param name="Category">What kind of problem: layout, consistency, accessibility, readability, links, media, file or rendering.</param>
/// <param name="Severity"><c>error</c>, <c>warning</c> or <c>info</c>.</param>
/// <param name="Slide">The slide number, or 0 for the whole deck.</param>
/// <param name="ElementId">The element's identifier, or 0.</param>
/// <param name="Message">What is wrong.</param>
/// <param name="Fix">How to fix it with the presentation tools, or <see langword="null"/>.</param>
internal sealed record PresentationIssue(string Category, string Severity, int Slide, uint ElementId, string Message, string Fix);
