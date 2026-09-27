namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// The language a piece of text is written in, as far as its script and its commonest words tell.
/// </summary>
/// <param name="Code">The BCP 47 primary language tag, such as <c>en</c>, or <c>und</c> when it cannot be told.</param>
/// <param name="Name">The language's English name.</param>
/// <param name="Script">The writing system most of the letters belong to.</param>
/// <param name="Confidence">How sure the detection is, from 0 to 1.</param>
/// <param name="Letters">The number of letters the detection read.</param>
/// <param name="Alternative">The next most likely language, when the evidence is close, or <see langword="null"/>.</param>
internal sealed record PdfLanguageDetection(string Code, string Name, string Script, double Confidence, int Letters, string Alternative);
