using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    public async Task ShouldLoadTelemetrySdk_OnFullPageLoad_WhenConnectionStringIsConfigured()
    {
        var sdkRequests = new ConcurrentQueue<string>();
        await Page.RouteAsync("**/appsettings.json", route => route.FulfillAsync(new RouteFulfillOptions
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                ApplicationInsights = new { ConnectionString = ConfiguredConnectionString }
            })
        }));
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
    }

    private static bool IsTelemetryUrl(string url) =>
        TelemetryEndpoints.SdkPattern.IsMatch(url) || TelemetryEndpoints.Pattern.IsMatch(url);

    private static bool IsTelemetryError(IConsoleMessage message) =>
        message.Type == "error" && (TelemetryText().IsMatch(message.Text) || IsTelemetryUrl(message.Location));

    [GeneratedRegex(@"app\s?insights|application\s?insights|instrumentation key", RegexOptions.IgnoreCase)]
    private static partial Regex TelemetryText();
}
