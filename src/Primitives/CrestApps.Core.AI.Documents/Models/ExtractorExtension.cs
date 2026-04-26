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

    public bool Equals(ExtractorExtension other)
    {
        return other is not null && string.Equals(Extension, other.Extension, StringComparison.Ordinal);
    }

    public bool Equals(string extension)
    {
        return extension is not null && string.Equals(Extension, Normalize(extension), StringComparison.Ordinal);
    }

    public override bool Equals(object obj)
    {
        return obj switch
        {
            ExtractorExtension ext => Equals(ext),
            string s => Equals(s),
            _ => false
        };
    }

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

    public override string ToString() => Extension;
}
