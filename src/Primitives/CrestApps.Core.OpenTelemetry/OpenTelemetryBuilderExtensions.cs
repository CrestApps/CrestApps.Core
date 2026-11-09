using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace CrestApps.Core.OpenTelemetry;

/// <summary>
/// Extension methods that add the CrestApps.Core telemetry to an OpenTelemetry pipeline.
/// </summary>
public static class OpenTelemetryBuilderExtensions
{
    // The libraries the framework calls into emit their own spans and metrics. Collecting them too keeps a chat
    // turn in one unbroken trace, from the SignalR hub down to the MCP server and the vector store.
    private static readonly string[] _sourceNames =
    [
        CrestAppsTelemetry.SourceName,
        CrestAppsTelemetry.SourceName + ".*",
        CrestAppsTelemetry.AIClientSourceName,
        "OpenAI.*",
        "Microsoft.AspNetCore.SignalR.Server",
        "Experimental.ModelContextProtocol",
        "A2A",
        "A2A.AspNetCore",
        "Npgsql",
    ];

    private static readonly string[] _meterNames =
    [
        CrestAppsTelemetry.SourceName,
        CrestAppsTelemetry.SourceName + ".*",
        CrestAppsTelemetry.AIClientSourceName,
        "OpenAI.*",
        "Microsoft.AspNetCore.Http.Connections",
        "Experimental.ModelContextProtocol",
        "A2A",
        "A2A.AspNetCore",
        "Npgsql",
        "Polly",
    ];

    /// <summary>
    /// Collects the CrestApps.Core spans and metrics, including the <c>CrestApps.Core.*</c> sub-areas such as
    /// templating, the <c>gen_ai.*</c> telemetry of the AI clients, and the telemetry of SignalR (hub spans and the
    /// active connection count), the OpenAI, MCP and A2A SDKs, Npgsql and Polly.
    /// </summary>
    /// <param name="builder">The OpenTelemetry builder returned by <c>AddOpenTelemetry()</c>.</param>
    /// <returns>The builder, for chaining an exporter such as <c>UseOtlpExporter()</c>.</returns>
    /// <remarks>
    /// This adds no exporter and no ASP.NET Core or HttpClient instrumentation, so it can be combined with
    /// whatever the host already configures without collecting anything twice. The Azure OpenAI chat client calls the
    /// OpenAI SDK directly; its <c>gen_ai.*</c> telemetry is collected once the host turns on the SDK's
    /// <c>OpenAI.Experimental.EnableOpenTelemetry</c> switch.
    /// </remarks>
    public static IOpenTelemetryBuilder AddCrestAppsOpenTelemetry(this IOpenTelemetryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WithTracing(tracing => tracing.AddSource(_sourceNames));
        builder.WithMetrics(metrics => metrics.AddMeter(_meterNames));

        return builder;
    }
}
