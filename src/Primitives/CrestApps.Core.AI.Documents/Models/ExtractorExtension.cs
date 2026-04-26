namespace CrestApps.Core.AI.Documents.Models;

/// <summary>
/// Represents the extractor Extension.
/// </summary>
public sealed class ExtractorExtension : IEquatable<ExtractorExtension>, IEquatable<string>
{
    /// <summary>
    /// Gets the extension.
    /// </summary>
    public string Extension { get; }

    /// <summary>
    /// Gets the embeddable.
    /// </summary>
    public bool Embeddable { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtractorExtension"/> class.
    /// </summary>
    /// <param name="extension">The extension.</param>
    /// <param name="embeddable">The embeddable.</param>
    public ExtractorExtension(
        string extension,
        bool embeddable = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        // Normalize once
        Extension = Normalize(extension);
        Embeddable = embeddable;
    }

    private static string Normalize(string extension)
    {
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        return ext.ToLowerInvariant();
    }

    /// <summary>
    /// Equalss the operation.
    /// </summary>
    /// <param name="other">The other.</param>
    public bool Equals(ExtractorExtension other)
    {
        return other is not null && string.Equals(Extension, other.Extension, StringComparison.Ordinal);
    }

    /// <summary>
    /// Equalss the operation.
    /// </summary>
    /// <param name="extension">The extension.</param>
    public bool Equals(string extension)
    {
        return extension is not null && string.Equals(Extension, Normalize(extension), StringComparison.Ordinal);
    }

    /// <summary>
    /// Equalss the operation.
    /// </summary>
    /// <param name="obj">The obj.</param>
    public override bool Equals(object obj)
    {
        return obj switch
        {
            ExtractorExtension ext => Equals(ext),
            string s => Equals(s),
            _ => false
        };
    }

    /// <summary>
    /// Gets hash code.
    /// </summary>
    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(Extension);
    }

    public static bool operator ==(ExtractorExtension left, ExtractorExtension right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(ExtractorExtension left, ExtractorExtension right)
    {
        return !Equals(left, right);
    }

    public static implicit operator string(ExtractorExtension ext)
    {
        return ext.Extension;
    }

    public static implicit operator ExtractorExtension(string extension)
    {
        return new(extension);
    }

    /// <summary>
    /// Tos string.
    /// </summary>
    public override string ToString() => Extension;
}
