using System.Text.RegularExpressions;

namespace ClearMeasure.Bootcamp.AcceptanceTests;

/// <summary>
/// The Application Insights ingestion endpoints the app's browser SDK posts to, and the answer the acceptance suite
/// gives in their place.
/// </summary>
public static partial class TelemetryEndpoints
{
    /// <summary>Matches a request to an Application Insights ingestion endpoint.</summary>
    public static Regex Pattern { get; } = IngestionEndpoint();

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
}
