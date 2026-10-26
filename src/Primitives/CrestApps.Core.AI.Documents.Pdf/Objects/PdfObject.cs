namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// An array or a dictionary: a value that can be stored as an object of its own in the file and referred
/// to from elsewhere.
/// </summary>
internal abstract class PdfObject : PdfItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfObject"/> class.
    /// </summary>
    /// <param name="owner">The document the object belongs to, or <see langword="null"/> when it is not attached yet.</param>
    protected PdfObject(PdfDocument owner)
    {
        Owner = owner;
    }

    /// <summary>
    /// Gets the document the object belongs to.
    /// </summary>
    public PdfDocument Owner { get; internal set; }

    /// <summary>
    /// Gets the reference to the object when it is stored as an object of its own, or <see langword="null"/>
    /// when it is written inside another object.
    /// </summary>
    public PdfReference Reference { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the object is stored as an object of its own.
    /// </summary>
    public bool IsIndirect => Reference is not null;
}
