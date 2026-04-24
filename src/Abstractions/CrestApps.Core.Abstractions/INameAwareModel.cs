namespace CrestApps.Core;

/// <summary>
/// Marks a model as having a unique technical name property,
/// enabling lookup and identification by a stable, human-friendly key.
/// </summary>
public interface INameAwareModel
{
    /// <summary>
    /// Gets the unique technical name for this model.
    /// </summary>
    string Name { get; }
}
