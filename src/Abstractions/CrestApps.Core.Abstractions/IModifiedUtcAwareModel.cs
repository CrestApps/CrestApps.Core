namespace CrestApps.Core;

/// <summary>
/// Marks a model as tracking the last UTC timestamp at which it was modified.
/// </summary>
public interface IModifiedUtcAwareModel
{
    /// <summary>
    /// Gets or sets the UTC timestamp when this model was last modified.
    /// </summary>
    DateTime? ModifiedUtc { get; set; }
}
