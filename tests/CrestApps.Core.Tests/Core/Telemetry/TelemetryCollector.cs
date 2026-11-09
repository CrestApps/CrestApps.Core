using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CrestApps.Core.Tests.Core.Telemetry;

/// <summary>
/// Collects what the CrestApps.Core meter and activity source emit while a test runs. Both are process-wide, so
/// tests filter by a tag value of their own rather than counting everything.
/// </summary>
internal sealed class TelemetryCollector : IDisposable
{
    private readonly MeterListener _meterListener = new();
    private readonly ActivityListener _activityListener;

    public TelemetryCollector()
    {
        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == CrestAppsTelemetry.SourceName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _meterListener.SetMeasurementEventCallback<double>(OnMeasurement);
        _meterListener.SetMeasurementEventCallback<long>(OnMeasurement);
        _meterListener.SetMeasurementEventCallback<int>(OnMeasurement);
        _meterListener.Start();

        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CrestAppsTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = Activities.Enqueue,
        };

        ActivitySource.AddActivityListener(_activityListener);
    }

    public ConcurrentQueue<Activity> Activities { get; } = new();

    public ConcurrentQueue<RecordedMeasurement> Measurements { get; } = new();

    public IReadOnlyList<RecordedMeasurement> GetMeasurements(string instrumentName, string tagName, object tagValue)
    {
        return Measurements
            .Where(measurement => measurement.InstrumentName == instrumentName && measurement.Tags.TryGetValue(tagName, out var value) && Equals(value, tagValue))
            .ToList();
    }

    public void Dispose()
    {
        _meterListener.Dispose();
        _activityListener.Dispose();
    }

    private void OnMeasurement<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object>> tags, object state)
        where T : struct
    {
        var recordedTags = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var tag in tags)
        {
            recordedTags[tag.Key] = tag.Value;
        }

        Measurements.Enqueue(new RecordedMeasurement(instrument.Name, Convert.ToDouble(value), recordedTags));
    }
}

internal sealed record RecordedMeasurement(string InstrumentName, double Value, IReadOnlyDictionary<string, object> Tags);
