using System.Text.Json;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.WebMcp;

/// <summary>
/// A selectable test tool that answers the text it receives.
/// </summary>
internal sealed class EchoTestTool : AIFunction
{
    public const string TheName = "echo_tool";

    private static readonly JsonElement _schema = JsonDocument.Parse("""{"type":"object","properties":{"text":{"type":"string"}}}""").RootElement.Clone();

    public override string Name => TheName;

    public override string Description => "Answers the text it receives.";

    public override JsonElement JsonSchema => _schema;

    protected override ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        arguments.TryGetValue("text", out var text);

        return ValueTask.FromResult<object>($"echo: {text}");
    }
}

/// <summary>
/// A selectable test tool that always throws.
/// </summary>
internal sealed class FailingTestTool : AIFunction
{
    public const string TheName = "failing_tool";

    public override string Name => TheName;

    public override string Description => "Always fails.";

    protected override ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Secret connection string in the message.");
    }
}

/// <summary>
/// A test tool registered as a system tool, which is never selectable.
/// </summary>
internal sealed class SystemTestTool : AIFunction
{
    public const string TheName = "system_tool";

    public override string Name => TheName;

    public override string Description => "A system tool.";

    protected override ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<object>("system");
    }
}

/// <summary>
/// A test tool hidden from the tool pickers, which is never selectable.
/// </summary>
internal sealed class HiddenTestTool : AIFunction
{
    public const string TheName = "hidden_tool";

    public override string Name => TheName;

    public override string Description => "A hidden tool.";

    protected override ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<object>("hidden");
    }
}

/// <summary>
/// A selectable test tool that reports whether it received the request services.
/// </summary>
internal sealed class ServicesProbeTestTool : AIFunction
{
    public const string TheName = "services_probe_tool";

    public override string Name => TheName;

    public override string Description => "Reports whether it received the request services.";

    protected override ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<object>(arguments.Services is null ? "no services" : "services");
    }
}
