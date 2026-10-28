namespace CrestApps.Core.AI.Documents.Pdf.Services;

/// <summary>
/// The installed face chosen for a family and style.
/// </summary>
/// <param name="FaceName">The face, to read with <see cref="SystemFontResolver.GetFont"/>.</param>
/// <param name="MustSimulateBold">Whether bold has to be simulated, because the font has no bold face.</param>
/// <param name="MustSimulateItalic">Whether italic has to be simulated, because the font has no italic face.</param>
/// <param name="IsRequestedFamily">Whether the face is of the family asked for, rather than one standing in for it.</param>
internal sealed record SystemFontFace(string FaceName, bool MustSimulateBold, bool MustSimulateItalic, bool IsRequestedFamily);
