using System.Diagnostics.Metrics;

namespace CrestApps.Core.Templates;

/// <summary>
/// The OpenTelemetry metrics of the templating library. The library stands on its own, so it reports under a
/// <c>CrestApps.Core.Templates</c> meter of its own, which <c>AddCrestAppsOpenTelemetry</c> collects together with
/// the other <c>CrestApps.Core.*</c> areas.
/// </summary>
public static class TemplateTelemetry
{
    /// <summary>
    /// The name of the templating meter.
    /// </summary>
    public const string MeterName = "CrestApps.Core.Templates";

    /// <summary>
    /// The meter the templating metrics are created on.
    /// </summary>
    public static readonly Meter Meter = new(MeterName, typeof(TemplateTelemetry).Assembly.GetName().Version?.ToString());

    /// <summary>
    /// The time it took to render a template, tagged with <c>outcome</c> (<c>ok</c>, <c>error</c> or
    /// <c>cancelled</c>). The template id is left off, because a host can add any number of templates.
    /// </summary>
    public static readonly Histogram<double> RenderDuration = Meter.CreateHistogram<double>(
        "crestapps.core.templates.render.duration",
        "s",
        "The time it took to render a template.",
        tags: null,
        advice: new InstrumentAdvice<double>
        {
            HistogramBucketBoundaries = [0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1],
        });
}
