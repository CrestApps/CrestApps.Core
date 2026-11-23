using CrestApps.Core.AI.Tooling;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// The tool approval settings of an AI profile, stored under <c>AIProfile.Settings["AIToolApprovalSettings"]</c>.
/// A profile with none asks before every call of a tool that requires an approval.
/// </summary>
/// <remarks>
/// The key is the type name: renaming this type, or the stored properties, would strand every stored value (the
/// read misses and returns the defaults, without an error). Moving its namespace is safe.
/// </remarks>
public sealed class AIToolApprovalSettings
{
    /// <summary>
    /// Gets or sets the rules by tool kind. The keys are <see cref="AIToolKind"/> names, such as
    /// <c>"Destructive"</c>, <c>"External"</c> and <c>"Security"</c>. A kind with no rule asks before every call, and
    /// a key that is not a kind is ignored.
    /// </summary>
    public Dictionary<string, ToolApprovalRule> Kinds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
