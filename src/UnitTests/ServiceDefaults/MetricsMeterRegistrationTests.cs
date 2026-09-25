using System.Diagnostics.Metrics;
using ChurchBulletin.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.ServiceDefaults;

[TestFixture]
public class MetricsMeterRegistrationTests
{
    [TestCase("ChurchBulletin.Application")]
    [TestCase("NServiceBus.Core.Pipeline.Incoming")]
    public void ShouldExportMeter_WhenServiceDefaultsAdded(string meterName)
    {
        CollectExportedMeterNames(meterName).ShouldContain(meterName);
    }

    [Test]
    public void ShouldNotExportMeter_WhenMeterNotRegistered()
    {
        const string meterName = "ChurchBulletin.UnregisteredTestMeter";

        CollectExportedMeterNames(meterName).ShouldNotContain(meterName);
    }

    private static List<string> CollectExportedMeterNames(string meterName)
    {
        var exporter = new StubMetricExporter();
        using var reader = new BaseExportingMetricReader(exporter);
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddReader(reader));
        using var host = builder.Build();
        host.Services.GetRequiredService<MeterProvider>();

        using var meter = new Meter(meterName);
        var counter = meter.CreateCounter<long>("test.meter.registration");
        counter.Add(1);
        reader.Collect();

        return exporter.MeterNames;
    }

    private sealed class StubMetricExporter : BaseExporter<Metric>
    {
        public List<string> MeterNames { get; } = [];

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                MeterNames.Add(metric.MeterName);
            }

            return ExportResult.Success;
        }
    }
}
