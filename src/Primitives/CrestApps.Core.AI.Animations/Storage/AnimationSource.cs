namespace CrestApps.Core.AI.Animations.Storage;

/// <summary>
/// What a stored animation was made from.
/// </summary>
/// <param name="AnimationId">The animation's identifier.</param>
/// <param name="SettingsJson">The arguments it was requested with: its title, length, size, seed and look.</param>
/// <param name="Script">The script it was drawn with.</param>
public sealed record AnimationSource(string AnimationId, string SettingsJson, string Script);
