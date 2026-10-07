using System.Text.RegularExpressions;

namespace ClearMeasure.Bootcamp.AcceptanceTests;

/// <summary>
/// The Application Insights hosts the app's browser can reach (the ingestion endpoints its SDK posts to and the hosts
/// the SDK itself is loaded from), and the answer the acceptance suite gives in place of an ingestion endpoint.
/// </summary>
public static partial class TelemetryEndpoints
{
    /// <summary>Matches a request to an Application Insights ingestion endpoint.</summary>
    public static Regex Pattern { get; } = IngestionEndpoint();

    /// <summary>Matches a request for the Application Insights JavaScript SDK on any host its loader tries.</summary>
    public static Regex SdkPattern { get; } = SdkHost();

    /// <summary>Answers a telemetry request (and its CORS preflight) as accepted, without sending it anywhere.</summary>
    /// <param name="route">The intercepted request.</param>
    public static Task AnswerAsync(IRoute route) => route.FulfillAsync(new RouteFulfillOptions
    {
        Status = 200,
        ContentType = "application/json",
        Headers = new Dictionary<string, string>
        {
            ["Access-Control-Allow-Origin"] = "*",
            ["Access-Control-Allow-Headers"] = "*",
            ["Access-Control-Allow-Methods"] = "POST, OPTIONS"
        },
        Body = """{"itemsReceived":1,"itemsAccepted":1,"errors":[]}"""
    });

    [GeneratedRegex(@"^https://([a-z0-9-]+\.)*(dc\.services\.visualstudio\.com|applicationinsights\.azure\.com)(:\d+)?/", RegexOptions.IgnoreCase)]
    private static partial Regex IngestionEndpoint();

    [GeneratedRegex(@"^https://(js\.monitor\.azure\.com|js\d?\.cdn\.(applicationinsights\.io|monitor\.azure\.com)|az416426\.vo\.msecnd\.net)(:\d+)?/", RegexOptions.IgnoreCase)]
    private static partial Regex SdkHost();
}
