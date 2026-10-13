namespace CrestApps.Core.AI.Animations.Rendering;

/// <summary>
/// The film look laid over every frame after it is drawn.
/// </summary>
/// <param name="Letterbox">Whether black bars crop the frame to a 2.39:1 widescreen picture.</param>
/// <param name="Vignette">How much the corners darken, from 0 to 1.</param>
/// <param name="Grain">How much film grain shows, from 0 to 1.</param>
internal sealed record CinematicLook(bool Letterbox, float Vignette, float Grain)
{
    /// <summary>
    /// Gets the look that changes nothing.
    /// </summary>
    public static CinematicLook None { get; } = new(false, 0, 0);

    /// <summary>
    /// Gets a value indicating whether the look changes anything.
    /// </summary>
    public bool IsNone => !Letterbox && Vignette <= 0 && Grain <= 0;
}
