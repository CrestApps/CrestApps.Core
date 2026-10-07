namespace CrestApps.Core.AI.Documents.Word.Editing;

/// <summary>
/// A picture resolved from what a tool call named, ready to be placed in a document.
/// </summary>
/// <param name="Bytes">The picture.</param>
/// <param name="Info">Its format and pixel size.</param>
/// <param name="Source">What the tool call named it by.</param>
internal sealed record WordImageData(byte[] Bytes, WordImageInfo Info, string Source);
