namespace CrestApps.Core.AI.Documents.Word.Workspace;

/// <summary>
/// A picture to show in the conversation.
/// </summary>
/// <param name="Title">What the picture shows.</param>
/// <param name="FileName">The file name it is stored under.</param>
/// <param name="Bytes">The picture.</param>
internal sealed record WordFigure(string Title, string FileName, byte[] Bytes);
