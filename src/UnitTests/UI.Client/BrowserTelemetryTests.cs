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
    public async Task StartAsync_ShouldStartTheSdkWithTheConnectionString_WhenConfigured()
    {
        var jsRuntime = new StubJsRuntime();

        await BrowserTelemetry.StartAsync(jsRuntime,
            new ConfigurationModel { AppInsightsConnectionString = RealConnectionString });

        var invocation = jsRuntime.Invocations.ShouldHaveSingleItem();
        invocation.Identifier.ShouldBe(BrowserTelemetry.StartFunction);
        invocation.Arguments.ShouldBe([RealConnectionString]);
    }

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
