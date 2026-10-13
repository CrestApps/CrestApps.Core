using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Animations.Storage;
using CrestApps.Core.AI.Documents.Generation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Animations.Tools;

/// <summary>
/// Returns the script and settings the conversation's latest video was made with, so a follow-up such as "make the
/// dog bigger" changes that video instead of starting a new one from scratch.
/// </summary>
internal sealed class GetAnimationSourceTool : AIFunction
{
    public const string TheName = "get_animation_source";

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {},
      "additionalProperties": false
    }
    """);

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "Returns the script, with its settings, that the latest video in this conversation was made with. Call it before changing a video the user already has.";

    /// <summary>
    /// Gets the JSON schema of the arguments.
    /// </summary>
    public override JsonElement JsonSchema => _jsonSchema;

    /// <summary>
    /// Gets the additional properties: the schema is not strict.
    /// </summary>
    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } =
        new Dictionary<string, object>()
        {
            ["Strict"] = false,
        };

    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var scope = GeneratedDocumentScope.Resolve();

        if (scope is null)
        {
            return "There is no conversation to look in.";
        }

        var source = await arguments.Services.GetRequiredService<IAnimationStore>().GetLatestSourceAsync(new AnimationOwner(scope.Value.ReferenceType, scope.Value.ReferenceId), cancellationToken);

        if (source?.Script is null)
        {
            return "This conversation has no video yet. Make a new one.";
        }

        return new StringBuilder()
            .AppendLine($"The latest video was drawn with this script. To change it, edit the script and call {RenderAnimationTool.TheName} with the whole updated script, keeping the settings below (length, size, seed and look) unless the user asked to change them.")
            .AppendLine()
            .AppendLine("Settings:")
            .AppendLine(source.SettingsJson ?? "{}")
            .AppendLine()
            .AppendLine("Script:")
            .AppendLine("```javascript")
            .AppendLine(source.Script)
            .Append("```")
            .ToString();
    }
}
