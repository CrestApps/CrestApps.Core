namespace CrestApps.Core.AI.Documents.Pdf.Workspace;

/// <summary>
/// A picture a tool shows the reader in the conversation.
/// </summary>
/// <param name="Title">What the picture is, used as its alternative text.</param>
/// <param name="FileName">The file name it is stored under.</param>
/// <param name="Bytes">The encoded picture.</param>
internal sealed record PdfFigure(string Title, string FileName, byte[] Bytes);
