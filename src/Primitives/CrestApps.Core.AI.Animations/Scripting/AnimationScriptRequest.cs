using CrestApps.Core.AI.Animations.Rendering;

namespace CrestApps.Core.AI.Animations.Scripting;

/// <summary>
/// An animation script and the video it should make.
/// </summary>
/// <param name="Script">The JavaScript the model wrote.</param>
/// <param name="Format">The size, rate and length of the frames.</param>
/// <param name="DurationSeconds">The length of the video in seconds.</param>
/// <param name="Seed">The seed for <c>Math.random</c>.</param>
/// <param name="Look">The film look laid over every frame.</param>
internal sealed record AnimationScriptRequest(string Script, AnimationFrameFormat Format, double DurationSeconds, int Seed, CinematicLook Look);
