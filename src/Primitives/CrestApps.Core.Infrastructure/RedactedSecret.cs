namespace CrestApps.Core.Infrastructure;

/// <summary>
/// Holds a secret value (for example, an API key) in a wrapper whose
/// <see cref="ToString"/> renders a masked sentinel instead of the raw value.
/// Use this anywhere a plaintext credential could otherwise leak through
/// generic logging, JSON serialization, or <c>object</c>-typed property bags.
/// Call <see cref="Reveal"/> at the credential boundary to read the secret.
/// </summary>
public sealed class RedactedSecret
{
    private const string Mask = "***";

    private readonly string _value;

    /// <summary>Initializes a new instance of the <see cref="RedactedSecret"/> class.</summary>
    /// <param name="value">The plaintext secret to wrap. May be <c>null</c> or empty.</param>
    public RedactedSecret(string value)
    {
        _value = value;
    }

    /// <summary>
    /// Returns the unredacted secret value. Use only at credential boundaries.
    /// </summary>
    public string Reveal() => _value;

    /// <summary>
    /// Returns <c>true</c> when the wrapped secret is <c>null</c> or empty.
    /// </summary>
    public bool IsEmpty => string.IsNullOrEmpty(_value);

    /// <summary>
    /// Returns a redacted string representation of the secret, never the raw value.
    /// </summary>
    public override string ToString() => Mask;

    /// <summary>
    /// Convenience factory that returns <c>null</c> for null/empty inputs to keep call sites tidy.
    /// </summary>
    /// <param name="value">The plaintext secret.</param>
    /// <returns>A <see cref="RedactedSecret"/> wrapping the value, or <c>null</c> when <paramref name="value"/> is null or empty.</returns>
    public static RedactedSecret CreateOrNull(string value)
    {
        return string.IsNullOrEmpty(value) ? null : new RedactedSecret(value);
    }
}
