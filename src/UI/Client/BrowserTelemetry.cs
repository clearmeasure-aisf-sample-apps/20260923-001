using System.Globalization;
using BlazorApplicationInsights;
using ClearMeasure.Bootcamp.Core;
using Microsoft.JSInterop;

namespace ClearMeasure.Bootcamp.UI.Client;

/// <summary>
/// Gates the Application Insights JavaScript SDK in the browser: it is registered, loaded and started only when a
/// connection string with a real instrumentation key is configured and the sampling percentage is not 0. Otherwise
/// the page makes no request to the SDK host or to an ingestion endpoint.
/// </summary>
public static class BrowserTelemetry
{
    /// <summary>Client configuration key that holds the Application Insights connection string.</summary>
    public const string ConnectionStringKey = "ApplicationInsights:ConnectionString";

    /// <summary>
    /// Configuration key that holds the share of the browser's telemetry that is sent, a number from 0 to 100. The
    /// server reads it from its own configuration and hands it to the client under the same key. It sits beside
    /// the connection string and says "browser" because the server's own telemetry does not read it.
    /// </summary>
    public const string SamplingPercentageKey = "ApplicationInsights:BrowserSamplingPercentage";

    /// <summary>Name of the function in <c>index.html</c> that loads and starts the SDK.</summary>
    public const string StartFunction = "startApplicationInsights";

    private const string InstrumentationKeyName = "InstrumentationKey";

    // Digits with an optional decimal point and exponent: no sign, no group separator. The exponent is there
    // because the server writes a very small percentage with one (0.00001 as 1E-05), and the browser has to read
    // what the server wrote.
    private const NumberStyles SamplingPercentageSyntax =
        NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowDecimalPoint
        | NumberStyles.AllowExponent;

    /// <summary>
    /// Tells whether a connection string names a real Application Insights resource: it has an
    /// <c>InstrumentationKey</c> that is a GUID other than the all-zero placeholder.
    /// </summary>
    /// <param name="connectionString">The configured connection string, if any.</param>
    public static bool IsConfigured(string? connectionString) =>
        Guid.TryParse(InstrumentationKey(connectionString), out var key) && key != Guid.Empty;

    /// <summary>
    /// Reads a configured sampling percentage: a number from 0 to 100, written with digits, an optional decimal
    /// point and an optional exponent. Anything else, a missing value included, is <c>null</c>: no sampling
    /// percentage is configured.
    /// </summary>
    /// <param name="value">The configured value, if any.</param>
    public static double? SamplingPercentage(string? value) =>
        double.TryParse(value, SamplingPercentageSyntax, CultureInfo.InvariantCulture, out var percentage)
        && percentage is >= 0 and <= 100
            ? percentage
            : null;

    /// <summary>
    /// Tells whether the browser sends telemetry: a connection string is configured
    /// (<see cref="IsConfigured"/>) and the sampling percentage is not 0. No sampling percentage sends everything.
    /// </summary>
    /// <param name="configuration">The client configuration.</param>
    public static bool IsEnabled(ConfigurationModel configuration) =>
        IsConfigured(configuration.AppInsightsConnectionString)
        && configuration.AppInsightsSamplingPercentage is not <= 0;

    /// <summary>
    /// Registers the client configuration and, only when telemetry is enabled, the Application Insights services.
    /// </summary>
    /// <param name="services">The client's service collection.</param>
    /// <param name="configuration">The client configuration that carries the telemetry settings.</param>
    public static void AddBrowserTelemetry(this IServiceCollection services, ConfigurationModel configuration)
    {
        services.AddSingleton(configuration);
        if (IsEnabled(configuration))
        {
            // No configuration callback: the settings go to the SDK when it is started, so the initialisation
            // component has nothing to update on an SDK that may still be loading.
            services.AddBlazorApplicationInsights();
        }
    }

    /// <summary>
    /// Loads and starts the SDK in the browser when telemetry is enabled, with the connection string and the
    /// sampling percentage (<c>null</c> when none is configured); does nothing otherwise.
    /// </summary>
    /// <param name="jsRuntime">The browser's JavaScript runtime.</param>
    /// <param name="configuration">The client configuration that carries the telemetry settings.</param>
    public static ValueTask StartAsync(IJSRuntime jsRuntime, ConfigurationModel configuration) =>
        IsEnabled(configuration)
            ? jsRuntime.InvokeVoidAsync(
                StartFunction,
                configuration.AppInsightsConnectionString,
                configuration.AppInsightsSamplingPercentage)
            : ValueTask.CompletedTask;

    private static string? InstrumentationKey(string? connectionString) =>
        (connectionString ?? string.Empty)
        .Split(';', StringSplitOptions.TrimEntries)
        .Select(segment => segment.Split('=', 2, StringSplitOptions.TrimEntries))
        .Where(pair => pair.Length == 2
                       && pair[0].Equals(InstrumentationKeyName, StringComparison.OrdinalIgnoreCase))
        .Select(pair => pair[1])
        .FirstOrDefault();
}
