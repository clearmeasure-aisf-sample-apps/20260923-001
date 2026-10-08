using System.Globalization;
using BlazorApplicationInsights.Interfaces;
using ClearMeasure.Bootcamp.Core;
using ClearMeasure.Bootcamp.UI.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.UI.Client;

[TestFixture]
public class BrowserTelemetryTests
{
    private const string RealConnectionString =
        "InstrumentationKey=3f2c9a51-7d1e-4b8a-9c64-2a5e8f0b1d37;IngestionEndpoint=https://example.in.applicationinsights.azure.com/";

    [TestCase(RealConnectionString)]
    [TestCase("InstrumentationKey=3f2c9a51-7d1e-4b8a-9c64-2a5e8f0b1d37")]
    [TestCase("IngestionEndpoint=https://example.in.applicationinsights.azure.com/; instrumentationkey = 3f2c9a51-7d1e-4b8a-9c64-2a5e8f0b1d37 ;")]
    public void IsConfigured_ShouldBeTrue_WhenConnectionStringHasARealInstrumentationKey(string connectionString)
    {
        BrowserTelemetry.IsConfigured(connectionString).ShouldBeTrue();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("InstrumentationKey=00000000-0000-0000-0000-000000000000")]
    [TestCase("InstrumentationKey=")]
    [TestCase("InstrumentationKey=not-a-key")]
    [TestCase("IngestionEndpoint=https://example.in.applicationinsights.azure.com/")]
    [TestCase("3f2c9a51-7d1e-4b8a-9c64-2a5e8f0b1d37")]
    public void IsConfigured_ShouldBeFalse_WhenConnectionStringHasNoRealInstrumentationKey(string? connectionString)
    {
        BrowserTelemetry.IsConfigured(connectionString).ShouldBeFalse();
    }

    [TestCase("0", 0)]
    [TestCase("25", 25)]
    [TestCase("100", 100)]
    [TestCase("33.3", 33.3)]
    [TestCase(" 5 ", 5)]
    [TestCase("0.00001", 0.00001)]
    [TestCase("1E-05", 0.00001)]
    [TestCase("2.5e1", 25)]
    [TestCase("1e2", 100)]
    public void SamplingPercentage_ShouldBeTheNumber_WhenTheValueIsFrom0To100(string value, double expected)
    {
        BrowserTelemetry.SamplingPercentage(value).ShouldBe(expected);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    [TestCase("-5")]
    [TestCase("100.5")]
    [TestCase("200")]
    [TestCase("half")]
    [TestCase("50%")]
    [TestCase("1,5")]
    [TestCase("1e3")]
    [TestCase("-1e1")]
    [TestCase("NaN")]
    [TestCase("Infinity")]
    public void SamplingPercentage_ShouldBeNull_WhenTheValueIsNotANumberFrom0To100(string? value)
    {
        BrowserTelemetry.SamplingPercentage(value).ShouldBeNull();
    }

    [Test]
    public void SamplingPercentage_ShouldReadADecimalPointWhateverTheCultureOfTheBrowser()
    {
        var culture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            BrowserTelemetry.SamplingPercentage("12.5").ShouldBe(12.5);
            BrowserTelemetry.SamplingPercentage("12,5").ShouldBeNull();
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }
    }

    [TestCase(null)]
    [TestCase(0.1)]
    [TestCase(25d)]
    [TestCase(100d)]
    public void IsEnabled_ShouldBeTrue_WhenAConnectionStringIsConfiguredAndTheSamplingPercentageIsNot0(
        double? samplingPercentage)
    {
        BrowserTelemetry.IsEnabled(Configured(samplingPercentage)).ShouldBeTrue();
    }

    [Test]
    public void IsEnabled_ShouldBeFalse_WhenTheSamplingPercentageIs0()
    {
        BrowserTelemetry.IsEnabled(Configured(0)).ShouldBeFalse();
    }

    [TestCase(null)]
    [TestCase(25d)]
    public void IsEnabled_ShouldBeFalse_WhenNoConnectionStringIsConfigured(double? samplingPercentage)
    {
        var configuration = new ConfigurationModel
        {
            AppInsightsConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000",
            AppInsightsSamplingPercentage = samplingPercentage
        };

        BrowserTelemetry.IsEnabled(configuration).ShouldBeFalse();
    }

    [Test]
    public void AddBrowserTelemetry_ShouldNotRegisterApplicationInsights_WhenTheSamplingPercentageIs0()
    {
        var services = new ServiceCollection();
        var configuration = Configured(0);

        services.AddBrowserTelemetry(configuration);

        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(IApplicationInsights));
        services.ShouldContain(descriptor => ReferenceEquals(descriptor.ImplementationInstance, configuration));
    }

    [Test]
    public void AddBrowserTelemetry_ShouldRegisterApplicationInsights_WhenTheSamplingPercentageIsAbove0()
    {
        var services = new ServiceCollection();

        services.AddBrowserTelemetry(Configured(25));

        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(IApplicationInsights));
    }

    [Test]
    public async Task StartAsync_ShouldNotCallTheBrowser_WhenTheSamplingPercentageIs0()
    {
        var jsRuntime = new StubJsRuntime();

        await BrowserTelemetry.StartAsync(jsRuntime, Configured(0));

        jsRuntime.Invocations.ShouldBeEmpty();
    }

    [TestCase(25d)]
    [TestCase(12.5)]
    [TestCase(100d)]
    public async Task StartAsync_ShouldStartTheSdkWithTheSamplingPercentage_WhenOneIsConfigured(
        double samplingPercentage)
    {
        var jsRuntime = new StubJsRuntime();

        await BrowserTelemetry.StartAsync(jsRuntime, Configured(samplingPercentage));

        var invocation = jsRuntime.Invocations.ShouldHaveSingleItem();
        invocation.Identifier.ShouldBe(BrowserTelemetry.StartFunction);
        invocation.Arguments.ShouldBe([RealConnectionString, samplingPercentage]);
    }

    [Test]
    public void AddBrowserTelemetry_ShouldNotRegisterApplicationInsights_WhenNothingIsConfigured()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationModel { AppInsightsConnectionString = "" };

        services.AddBrowserTelemetry(configuration);

        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(IApplicationInsights));
        services.ShouldContain(descriptor => ReferenceEquals(descriptor.ImplementationInstance, configuration));
    }

    [Test]
    public void AddBrowserTelemetry_ShouldRegisterApplicationInsights_WhenConnectionStringIsConfigured()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationModel { AppInsightsConnectionString = RealConnectionString };

        services.AddBrowserTelemetry(configuration);

        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(IApplicationInsights));
        services.ShouldContain(descriptor => ReferenceEquals(descriptor.ImplementationInstance, configuration));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("InstrumentationKey=00000000-0000-0000-0000-000000000000")]
    public async Task StartAsync_ShouldNotCallTheBrowser_WhenNothingIsConfigured(string? connectionString)
    {
        var jsRuntime = new StubJsRuntime();

        await BrowserTelemetry.StartAsync(jsRuntime,
            new ConfigurationModel { AppInsightsConnectionString = connectionString });

        jsRuntime.Invocations.ShouldBeEmpty();
    }

    [Test]
    public async Task StartAsync_ShouldStartTheSdkWithTheConnectionStringAndNoSamplingPercentage_WhenNoneIsConfigured()
    {
        var jsRuntime = new StubJsRuntime();

        await BrowserTelemetry.StartAsync(jsRuntime,
            new ConfigurationModel { AppInsightsConnectionString = RealConnectionString });

        var invocation = jsRuntime.Invocations.ShouldHaveSingleItem();
        invocation.Identifier.ShouldBe(BrowserTelemetry.StartFunction);
        invocation.Arguments.ShouldBe([RealConnectionString, null]);
    }

    private static ConfigurationModel Configured(double? samplingPercentage) =>
        new()
        {
            AppInsightsConnectionString = RealConnectionString,
            AppInsightsSamplingPercentage = samplingPercentage
        };

    private sealed class StubJsRuntime : IJSRuntime
    {
        public List<(string Identifier, object?[] Arguments)> Invocations { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken,
            object?[]? args)
        {
            Invocations.Add((identifier, args ?? []));
            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
