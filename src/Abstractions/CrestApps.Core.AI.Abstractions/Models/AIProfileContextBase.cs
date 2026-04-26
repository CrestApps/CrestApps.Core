namespace CrestApps.Core.AI.Models;

/// <summary>
/// Base context class that carries an <see cref="AIProfile"/> reference for profile-scoped operations.
/// </summary>
public abstract class AIProfileContextBase
{
    /// <summary>
    /// Gets the AI profile associated with this context.
    /// </summary>
    public AIProfile Profile { get; }

    public AIProfileContextBase(AIProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        Profile = profile;
    }
}
