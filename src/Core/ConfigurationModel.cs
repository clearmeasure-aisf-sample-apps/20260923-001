// ReSharper disable PropertyCanBeMadeInitOnly.Global -- Qodana P5 (#9440): NHibernate proxy / System.Text.Json set-by-convention requires mutable setters

namespace ClearMeasure.Bootcamp.Core;

public class ConfigurationModel
{
    public string? AppInsightsConnectionString { get; set; }

    /// <summary>
    /// Share of the browser's telemetry that is sent, from 0 to 100; <c>null</c> when none is configured, which
    /// sends everything. With 0 the browser loads no telemetry SDK at all.
    /// </summary>
    public double? AppInsightsSamplingPercentage { get; set; }
}