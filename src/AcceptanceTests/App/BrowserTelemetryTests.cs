using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClearMeasure.Bootcamp.UI.Client;
using ClearMeasure.Bootcamp.UI.Server;
using ClearMeasure.Bootcamp.UI.Shared;

namespace ClearMeasure.Bootcamp.AcceptanceTests.App;

[TestFixture]
public partial class BrowserTelemetryTests : AcceptanceTestBase
{
    private const string ConfiguredConnectionString =
        "InstrumentationKey=3f2c9a51-7d1e-4b8a-9c64-2a5e8f0b1d37;IngestionEndpoint=https://acceptance.in.applicationinsights.azure.com/";

    // Stands in for the SDK script: marks the loader's object as initialised, so the loader neither tries its
    // other hosts nor reports a load failure, and nothing is sent anywhere.
    private const string StubSdk = "window.appInsights.core = {};";

    // The section of the client settings that holds what the server adds for the browser's telemetry.
    private const string TelemetrySection = "ApplicationInsights";

    private static readonly JsonNodeOptions CaseInsensitiveNames = new() { PropertyNameCaseInsensitive = true };

    [Test, Retry(2)]
    public async Task ShouldSendNoTelemetryRequest_OnFullPageLoadAndNavigation_WhenNoConnectionStringIsConfigured()
    {
        RequireTheSuiteServer();

        await AssertNoTelemetryOnFullPageLoadAndNavigationAsync();
    }

    [Test, Retry(2)]
    public async Task ShouldServeClientSettingsWithoutAConnectionString_WhenTheServerHasOnlyThePlaceholder()
    {
        RequireTheSuiteServer();

        var response = await Page.APIRequest.GetAsync(ClientSettings.RequestPath);

        response.Status.ShouldBe(200);
        var settings = Settings(await response.TextAsync());
        settings.ShouldNotBeEmpty();
        settings.Keys.ShouldNotContain(key => key.Contains(TelemetrySection, StringComparison.OrdinalIgnoreCase));
    }

    [Test, Retry(2)]
    public async Task ShouldLoadTelemetrySdk_OnFullPageLoad_WhenTheServerGeneratesSettingsWithItsConnectionString()
    {
        await WaitForThePageTheFixtureOpenedAsync();
        var servedSettings = await ServeTheSettingsOfAConfiguredServerAsync(samplingPercentage: null);
        var sdkRequests = await StubTheSdkAsync();

        await Page.ReloadAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.GetByTestId(nameof(MainLayout.Elements.CopyrightFooter)).WaitForAsync();
        await Page.WaitForFunctionAsync("() => window.appInsights && window.appInsights.core");

        var connectionString = await Page.EvaluateAsync<string>("() => window.appInsights.config.connectionString");
        connectionString.ShouldBe(ConfiguredConnectionString);
        var sdkHasASamplingPercentage =
            await Page.EvaluateAsync<bool>("() => 'samplingPercentage' in window.appInsights.config");
        sdkHasASamplingPercentage.ShouldBeFalse();
        sdkRequests.ShouldHaveSingleItem().ShouldStartWith("https://js.monitor.azure.com/");
        servedSettings.ShouldNotBeEmpty();
        var served = Settings(servedSettings.First());
        served[BrowserTelemetry.ConnectionStringKey].ShouldBe(ConfiguredConnectionString);
        served.ShouldNotContainKey(BrowserTelemetry.SamplingPercentageKey);
        served.Count.ShouldBeGreaterThan(1);
    }

    [Test, Retry(2)]
    public async Task ShouldStartTelemetrySdkWithTheSamplingPercentage_WhenTheServerGeneratesSettingsWithOne()
    {
        await WaitForThePageTheFixtureOpenedAsync();
        var servedSettings = await ServeTheSettingsOfAConfiguredServerAsync(samplingPercentage: 25);
        var sdkRequests = await StubTheSdkAsync();

        await Page.ReloadAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.GetByTestId(nameof(MainLayout.Elements.CopyrightFooter)).WaitForAsync();
        await Page.WaitForFunctionAsync("() => window.appInsights && window.appInsights.core");

        var samplingPercentage =
            await Page.EvaluateAsync<double>("() => window.appInsights.config.samplingPercentage");
        samplingPercentage.ShouldBe(25);
        var connectionString = await Page.EvaluateAsync<string>("() => window.appInsights.config.connectionString");
        connectionString.ShouldBe(ConfiguredConnectionString);
        sdkRequests.ShouldHaveSingleItem().ShouldStartWith("https://js.monitor.azure.com/");
        servedSettings.ShouldNotBeEmpty();
        var served = Settings(servedSettings.First());
        served[BrowserTelemetry.ConnectionStringKey].ShouldBe(ConfiguredConnectionString);
        served[BrowserTelemetry.SamplingPercentageKey].ShouldBe("25");
    }

    [Test, Retry(2)]
    public async Task ShouldSendNoTelemetryRequest_OnFullPageLoadAndNavigation_WhenTheServerGeneratesSettingsWithASamplingPercentageOf0()
    {
        await WaitForThePageTheFixtureOpenedAsync();
        var servedSettings = await ServeTheSettingsOfAConfiguredServerAsync(samplingPercentage: 0);
        // Against a deployed server the page the fixture opened has the SDK, which may send what it still holds as
        // the page goes away. This reload replaces that page with one that got the settings above; only what
        // happens after it is counted.
        await Page.ReloadAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.GetByTestId(nameof(MainLayout.Elements.CopyrightFooter)).WaitForAsync();

        await AssertNoTelemetryOnFullPageLoadAndNavigationAsync();

        servedSettings.ShouldNotBeEmpty();
        var served = Settings(servedSettings.First());
        served[BrowserTelemetry.ConnectionStringKey].ShouldBe(ConfiguredConnectionString);
        served[BrowserTelemetry.SamplingPercentageKey].ShouldBe("0");
    }

    // Reloads the page, logs in and moves to another page: nothing asks for the SDK, nothing goes to an ingestion
    // endpoint, nothing complains about telemetry, and the page has no SDK object.
    private async Task AssertNoTelemetryOnFullPageLoadAndNavigationAsync()
    {
        var telemetryRequests = new ConcurrentQueue<string>();
        var telemetryErrors = new ConcurrentQueue<string>();
        Page.Request += (_, request) =>
        {
            if (IsTelemetryUrl(request.Url)) telemetryRequests.Enqueue(request.Url);
        };
        Page.Console += (_, message) =>
        {
            if (IsTelemetryError(message)) telemetryErrors.Enqueue(message.Text);
        };

        await Page.ReloadAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.GetByTestId(nameof(MainLayout.Elements.CopyrightFooter)).WaitForAsync();
        await LoginAsCurrentUser();
        await Click(nameof(NavMenu.Elements.Search));
        await Page.WaitForURLAsync("**/workorder/search");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        telemetryRequests.ShouldBeEmpty();
        telemetryErrors.ShouldBeEmpty();
        var sdkPresent = await Page.EvaluateAsync<bool>("() => typeof window.appInsights !== 'undefined'");
        sdkPresent.ShouldBeFalse();
    }

    // A deployed server has a connection string of its own, so the page the fixture opened loads the SDK too.
    // That load has to be over before a test's routes go in: only what the test itself loads is counted.
    private async Task WaitForThePageTheFixtureOpenedAsync()
    {
        await Page.GetByTestId(nameof(MainLayout.Elements.CopyrightFooter)).WaitForAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    // Hands the browser, from now on, the settings of a server that has the test's connection string and the given
    // sampling percentage (none: the setting is not there). Returns what was handed over, one entry per request.
    private async Task<ConcurrentQueue<string>> ServeTheSettingsOfAConfiguredServerAsync(double? samplingPercentage)
    {
        var servedSettings = new ConcurrentQueue<string>();
        await Page.RouteAsync($"**{ClientSettings.RequestPath}", async route =>
        {
            var settings = Generate(await (await route.FetchAsync()).TextAsync(), samplingPercentage);
            servedSettings.Enqueue(settings ?? string.Empty);
            await route.FulfillAsync(new RouteFulfillOptions { ContentType = "application/json", Body = settings });
        });
        return servedSettings;
    }

    // Answers every request for the SDK script with the stub. Returns the requests, one entry per request.
    private async Task<ConcurrentQueue<string>> StubTheSdkAsync()
    {
        var sdkRequests = new ConcurrentQueue<string>();
        await Page.RouteAsync(TelemetryEndpoints.SdkPattern, route =>
        {
            sdkRequests.Enqueue(route.Request.Url);
            return route.FulfillAsync(new RouteFulfillOptions { ContentType = "text/javascript", Body = StubSdk });
        });
        return sdkRequests;
    }

    // What the server's own code (ClientSettings) generates for the test's connection string and the given sampling
    // percentage. It starts from the client's static settings: what the server under test serves, without the
    // telemetry settings that server added. The suite's server adds none; a deployed server adds its environment's
    // connection string and, when its environment has one, sampling percentage, and neither may decide a test.
    private static string? Generate(string servedByTheServerUnderTest, double? samplingPercentage)
    {
        if (JsonNode.Parse(servedByTheServerUnderTest, CaseInsensitiveNames) is not JsonObject staticSettings)
        {
            return null;
        }

        staticSettings.Remove(TelemetrySection);
        return ClientSettings.Merge(staticSettings.ToJsonString(), ConfiguredConnectionString, samplingPercentage);
    }


    // The suite's own server runs with the all-zero placeholder (ServerFixture); a deployed server has its
    // environment's connection string and serves it to the browser.
    private static void RequireTheSuiteServer()
    {
        if (!ServerFixture.StartLocalServer)
        {
            Assert.Ignore("Requires the suite's own server, which has no Application Insights connection string");
        }
    }

    // Reads settings the way the client does: through the JSON configuration provider.
    private static Dictionary<string, string?> Settings(string json) =>
        new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build()
            .AsEnumerable()
            .Where(setting => setting.Value is not null)
            .ToDictionary(setting => setting.Key, setting => setting.Value, StringComparer.OrdinalIgnoreCase);

    private static bool IsTelemetryUrl(string url) =>
        TelemetryEndpoints.SdkPattern.IsMatch(url) || TelemetryEndpoints.Pattern.IsMatch(url);

    private static bool IsTelemetryError(IConsoleMessage message) =>
        message.Type == "error" && (TelemetryText().IsMatch(message.Text) || IsTelemetryUrl(message.Location));

    [GeneratedRegex(@"app\s?insights|application\s?insights|instrumentation key", RegexOptions.IgnoreCase)]
    private static partial Regex TelemetryText();
}
