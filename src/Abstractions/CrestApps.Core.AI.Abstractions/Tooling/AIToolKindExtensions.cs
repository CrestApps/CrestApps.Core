namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Provides the rules that follow from an <see cref="AIToolKind"/>.
/// </summary>
public static class AIToolKindExtensions
{
    /// <summary>
    /// Determines whether a tool of this kind needs a person's approval before each call, unless its registration
    /// says otherwise.
    /// </summary>
    /// <param name="kind">The kind of the tool.</param>
    /// <returns><see langword="true"/> for <see cref="AIToolKind.Destructive"/>, <see cref="AIToolKind.External"/> and <see cref="AIToolKind.Security"/>.</returns>
    public static bool RequiresApproval(this AIToolKind kind)
    {
        return kind is AIToolKind.Destructive or AIToolKind.External or AIToolKind.Security;
    }

    /// <summary>
    /// Determines whether a setting that exposes every tool (such as <c>ExposeAllTools</c> of the MCP server and of
    /// WebMCP) includes a tool of this kind. A tool that is left out is still exposed when the setting names it.
    /// </summary>
    /// <param name="kind">The kind of the tool.</param>
    /// <returns><see langword="false"/> for <see cref="AIToolKind.Destructive"/> and <see cref="AIToolKind.Security"/>; otherwise <see langword="true"/>.</returns>
    public static bool IsExposedWithAllTools(this AIToolKind kind)
    {
        return kind is not (AIToolKind.Destructive or AIToolKind.Security);
    }
}
