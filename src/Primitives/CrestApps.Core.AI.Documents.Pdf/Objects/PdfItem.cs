namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// One value a PDF file is made of: a number, a name, a string, an array, a dictionary, a reference to an
/// object stored elsewhere in the file, or null.
/// </summary>
/// <remarks>
/// The model is the package's own, read through PdfPig and written by <see cref="PdfObjectWriter"/>, so no
/// other PDF library is needed to change a file and save it.
/// </remarks>
internal abstract class PdfItem
{
    /// <summary>
    /// Writes the item as PDF syntax, as it would appear inside an object, so a message can quote it.
    /// </summary>
    /// <returns>The syntax, for example <c>/FlateDecode</c> or <c>12 0 R</c>.</returns>
    public override string ToString()
    {
        return PdfObjectWriter.ToSyntax(this);
    }
}
