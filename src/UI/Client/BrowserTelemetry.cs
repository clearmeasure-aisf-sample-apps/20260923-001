using BlazorApplicationInsights;
using ClearMeasure.Bootcamp.Core;
using Microsoft.JSInterop;

namespace ClearMeasure.Bootcamp.UI.Client;

/// <summary>
/// Gates the Application Insights JavaScript SDK in the browser: it is registered, loaded and started only when a
/// connection string with a real instrumentation key is configured. Without one the page makes no request to the
/// SDK host or to an ingestion endpoint.
/// </summary>
public static class BrowserTelemetry
{
    /// <summary>Client configuration key that holds the Application Insights connection string.</summary>
    public const string ConnectionStringKey = "ApplicationInsights:ConnectionString";

    /// <summary>Name of the function in <c>index.html</c> that loads and starts the SDK.</summary>
    public const string StartFunction = "startApplicationInsights";

    private const string InstrumentationKeyName = "InstrumentationKey";

    /// <summary>
    /// Tells whether a connection string names a real Application Insights resource: it has an
    /// <c>InstrumentationKey</c> that is a GUID other than the all-zero placeholder.
    /// </summary>
    /// <param name="connectionString">The configured connection string, if any.</param>
    public static bool IsConfigured(string? connectionString) =>
        Guid.TryParse(InstrumentationKey(connectionString), out var key) && key != Guid.Empty;

    /// <summary>
    /// Registers the client configuration and, only when telemetry is configured, the Application Insights services.
    /// </summary>
    /// <param name="services">The client's service collection.</param>
    /// <param name="configuration">The client configuration that carries the connection string.</param>
    public static void AddBrowserTelemetry(this IServiceCollection services, ConfigurationModel configuration)
    {
        services.AddSingleton(configuration);
        if (IsConfigured(configuration.AppInsightsConnectionString))
        {
            // No configuration callback: the connection string goes to the SDK when it is started, so the
            // initialisation component has nothing to update on an SDK that may still be loading.
            services.AddBlazorApplicationInsights();
        }
    }

    /// <summary>
    /// Loads and starts the SDK in the browser when telemetry is configured; does nothing otherwise.
    /// </summary>
    /// <param name="jsRuntime">The browser's JavaScript runtime.</param>
    /// <param name="configuration">The client configuration that carries the connection string.</param>
    public static ValueTask StartAsync(IJSRuntime jsRuntime, ConfigurationModel configuration) =>
        IsConfigured(configuration.AppInsightsConnectionString)
            ? jsRuntime.InvokeVoidAsync(StartFunction, configuration.AppInsightsConnectionString)
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
