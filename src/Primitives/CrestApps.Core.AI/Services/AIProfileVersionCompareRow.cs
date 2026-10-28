namespace CrestApps.Core.AI.Services;

/// <summary>
/// One field or setting of two copies of an AI profile, side by side.
/// </summary>
/// <param name="Label">The setting.</param>
/// <param name="Then">The value on the left, or <see langword="null"/> when it is not set.</param>
/// <param name="Now">The value on the right, or <see langword="null"/> when it is not set.</param>
/// <param name="Multiline">Whether the values are long text.</param>
/// <param name="IsCode">Whether the values are JSON.</param>
public sealed record AIProfileVersionCompareRow(string Label, string Then, string Now, bool Multiline = false, bool IsCode = false)
{
    /// <summary>
    /// Gets the group of the profile editor the setting belongs to, one of <see cref="AIProfileVersionComparer.Sections"/>.
    /// </summary>
    public string Section { get; init; }

    /// <summary>
    /// Gets whether the two values differ.
    /// </summary>
    public bool Changed => !string.Equals(Then ?? string.Empty, Now ?? string.Empty, StringComparison.Ordinal);
}
