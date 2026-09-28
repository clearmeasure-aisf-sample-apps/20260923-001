using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChurchBulletin.ServiceDefaults;

/// <summary>
/// Background service that writes telemetry data to local text files.
/// </summary>
public class LocalTelemetryFileWriter : BackgroundService, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private bool _disposed;

    public string TelemetryLogDirectory { get; }

    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener;
    private readonly LocalTelemetryOptions _options;

    private RotatingJsonlFileWriter? _tracesWriter;
    private RotatingJsonlFileWriter? _eventsWriter;
    private RotatingJsonlFileWriter? _logsWriter;
    private RotatingJsonlFileWriter? _metricsWriter;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalTelemetryFileWriter"/> class.
    /// </summary>
    public LocalTelemetryFileWriter(IConfiguration? configuration = null)
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = SampleAllDataAndRecorded,
            ActivityStarted = OnActivityStarted,
            ActivityStopped = OnActivityStopped
        };

        _meterListener = new MeterListener
        {
            InstrumentPublished = OnInstrumentPublished
        };
        _meterListener.SetMeasurementEventCallback<int>(OnMeasurementRecorded);
        _meterListener.SetMeasurementEventCallback<long>(OnMeasurementRecorded);
        _meterListener.SetMeasurementEventCallback<float>(OnMeasurementRecorded);
        _meterListener.SetMeasurementEventCallback<double>(OnMeasurementRecorded);
        _meterListener.SetMeasurementEventCallback<decimal>(OnMeasurementRecorded);

        _options = LocalTelemetryOptions.FromConfiguration(configuration);

        var configuredPath = configuration?["LocalTelemetry:LogDirectory"];

        if (!string.IsNullOrEmpty(configuredPath))
        {
            TelemetryLogDirectory = configuredPath;
        }
        else
        {
            var basePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Directory.GetCurrentDirectory();
            TelemetryLogDirectory = Path.Combine(basePath, "logs");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Directory.Exists(TelemetryLogDirectory))
        {
            Directory.CreateDirectory(TelemetryLogDirectory);
        }

        CleanupOldFiles();
        TelemetryFileMaintenance.EnforceTotalSizeCap(TelemetryLogDirectory, _options.MaxTotalSizeBytes);

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        _tracesWriter = CreateWriter("traces", timestamp);
        _eventsWriter = CreateWriter("events", timestamp);
        _logsWriter = CreateWriter("logs", timestamp);
        _metricsWriter = CreateWriter("metrics", timestamp);

        ActivitySource.AddActivityListener(_activityListener);
        _meterListener.Start();

        Trace.WriteLine($"Local telemetry file writer started. Writing to {Path.GetFullPath(TelemetryLogDirectory)}");

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            // Expected when stopping
        }
    }

    private RotatingJsonlFileWriter CreateWriter(string prefix, string timestamp) =>
        new(TelemetryLogDirectory, prefix, timestamp, _options.MaxFileSizeBytes, _options.MaxTotalSizeBytes);

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await DisposeAsync();
        await base.StopAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _activityListener.Dispose();
        _meterListener.Dispose();

        await TelemetryFileMaintenance.DisposeWritersAsync(
            _tracesWriter, _eventsWriter, _logsWriter, _metricsWriter);

        GC.SuppressFinalize(this);
    }

    public void WriteTraceEntry(Activity activity, string status)
    {
        if (_tracesWriter == null) return;

        WriteJsonLine(_tracesWriter, TraceEntryMapper.FromActivity(activity, status));
    }

    public void WriteEventEntry(Activity activity, ActivityEvent evt)
    {
        if (_eventsWriter == null) return;

        WriteJsonLine(_eventsWriter, new EventEntry(activity, evt));
    }

    public void WriteLogEntry(LogLevel level, string category, string message, Exception? exception = null)
    {
        if (_logsWriter == null)
        {
            return;
        }

        WriteJsonLine(_logsWriter, CreateLogEntry(level, category, message, exception));
    }

    private static LogEntry CreateLogEntry(LogLevel level, string category, string message, Exception? exception) =>
        new()
        {
            Timestamp = DateTime.UtcNow,
            Level = level.ToString(),
            Category = category,
            Message = message,
            Exception = exception == null ? null : new LogEntryError(exception)
        };

    private static void WriteJsonLine<T>(RotatingJsonlFileWriter writer, T entry)
    {
        try
        {
            writer.WriteLine(JsonSerializer.Serialize(entry, JsonOptions));
        }
        catch (Exception)
        {
            // Best-effort local telemetry file writes must never affect the application.
        }
    }

    public void WriteMetricEntry(string name, double value, string unit = "", IDictionary<string, object?>? tags = null)
    {
        if (_metricsWriter == null) return;

        WriteJsonLine(_metricsWriter, new MetricEntry(name, value, unit, tags));
    }

    private void CleanupOldFiles(int retentionDays = 7) =>
        TelemetryFileMaintenance.DeleteFilesOlderThan(TelemetryLogDirectory, retentionDays);

    private void OnActivityStarted(Activity activity)
    {
        WriteTraceEntry(activity, "STARTED");
    }

    private static ActivitySamplingResult SampleAllDataAndRecorded(ref ActivityCreationOptions<ActivityContext> _) =>
        ActivitySamplingResult.AllDataAndRecorded;

    private void OnActivityStopped(Activity activity)
    {
        WriteTraceEntry(activity, "STOPPED");

        foreach (var evt in activity.Events)
        {
            WriteEventEntry(activity, evt);
        }
    }

    private void OnInstrumentPublished(Instrument instrument, MeterListener listener)
    {
        // Listen to all instruments
        listener.EnableMeasurementEvents(instrument);
    }

    private void OnMeasurementRecorded<T>(
        Instrument instrument,
        T measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state) where T : struct
    {
        var tagsDictionary = new Dictionary<string, object?>();

        foreach (var tag in tags)
        {
            tagsDictionary[tag.Key] = tag.Value;
        }

        tagsDictionary["meter.name"] = instrument.Meter.Name;
        tagsDictionary["meter.version"] = instrument.Meter.Version;
        tagsDictionary["instrument.type"] = instrument.GetType().Name;

        var value = Convert.ToDouble(measurement);
        WriteMetricEntry(instrument.Name, value, instrument.Unit ?? "", tagsDictionary);
    }
}
