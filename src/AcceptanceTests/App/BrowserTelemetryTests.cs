using System.Collections.Concurrent;
using System.Text;
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

    [Test, Retry(2)]
    public async Task ShouldSendNoTelemetryRequest_OnFullPageLoadAndNavigation_WhenNoConnectionStringIsConfigured()
    {
        RequireTheSuiteServer();
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

    [Test, Retry(2)]
    public async Task ShouldServeClientSettingsWithoutAConnectionString_WhenTheServerHasOnlyThePlaceholder()
    {
        RequireTheSuiteServer();

        var response = await Page.APIRequest.GetAsync(ClientSettings.RequestPath);

        response.Status.ShouldBe(200);
        var settings = Settings(await response.TextAsync());
        settings.ShouldNotBeEmpty();
        settings.Keys.ShouldNotContain(key => key.Contains("ApplicationInsights", StringComparison.OrdinalIgnoreCase));
    }

    [Test, Retry(2)]
    public async Task ShouldLoadTelemetrySdk_OnFullPageLoad_WhenTheServerGeneratesSettingsWithItsConnectionString()
    {
        var sdkRequests = new ConcurrentQueue<string>();
        var servedSettings = new ConcurrentQueue<string>();
        // A deployed server has a connection string of its own, so the page the fixture opened loads the SDK too.
        // That load has to be over before the routes below go in: only the reload under test is counted.
        await Page.GetByTestId(nameof(MainLayout.Elements.CopyrightFooter)).WaitForAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // The suite's one server has no connection string, so the browser is handed what a server that has one
        // generates: the settings this server serves, merged by the server's own code (ClientSettings).
        await Page.RouteAsync($"**{ClientSettings.RequestPath}", async route =>
        {
            var staticSettings = await (await route.FetchAsync()).TextAsync();
            var settings = ClientSettings.Merge(staticSettings, ConfiguredConnectionString);
            servedSettings.Enqueue(settings ?? string.Empty);
            await route.FulfillAsync(new RouteFulfillOptions { ContentType = "application/json", Body = settings });
        });
        await Page.RouteAsync(TelemetryEndpoints.SdkPattern, route =>
        {
            sdkRequests.Enqueue(route.Request.Url);
            return route.FulfillAsync(new RouteFulfillOptions { ContentType = "text/javascript", Body = StubSdk });
        });

        await Page.ReloadAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.GetByTestId(nameof(MainLayout.Elements.CopyrightFooter)).WaitForAsync();
        await Page.WaitForFunctionAsync("() => window.appInsights && window.appInsights.core");

        var connectionString = await Page.EvaluateAsync<string>("() => window.appInsights.config.connectionString");
        connectionString.ShouldBe(ConfiguredConnectionString);
        sdkRequests.ShouldHaveSingleItem().ShouldStartWith("https://js.monitor.azure.com/");
        servedSettings.ShouldNotBeEmpty();
        var served = Settings(servedSettings.First());
        served[BrowserTelemetry.ConnectionStringKey].ShouldBe(ConfiguredConnectionString);
        served.Count.ShouldBeGreaterThan(1);
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
