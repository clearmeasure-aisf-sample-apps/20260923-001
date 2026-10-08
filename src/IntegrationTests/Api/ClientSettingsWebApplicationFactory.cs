using System.Collections.Concurrent;
using ClearMeasure.Bootcamp.UI.Client;
using ClearMeasure.Bootcamp.UI.Server;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace ClearMeasure.Bootcamp.IntegrationTests.Api;

/// <summary>
/// Hosts UI.Server over the client's own web root, as a published image has it, with a given Application Insights
/// connection string in the server's configuration and, when one is given, a browser sampling percentage, for the
/// client settings (<c>/appsettings.json</c>) tests. API-key authentication is on and the server holds values the
/// browser must never receive.
/// </summary>
/// <param name="serverConnectionString">The server's Application Insights connection string.</param>
/// <param name="samplingPercentage">
/// The server's value for the browser's sampling percentage; <c>null</c> leaves the setting out of the configuration.
/// </param>
public sealed class ClientSettingsWebApplicationFactory(
    string serverConnectionString,
    string? samplingPercentage = null)
    : WebApplicationFactory<UiServerWebApplicationMarker>
{
    public const string ServerApiKey = "client-settings-server-only-api-key";
    public const string ServerSqlConnectionString = "Data Source=:memory:";
    public const string ServerOpenAiKey = "client-settings-server-only-openai-key";

    /// <summary>The client's static web root in the source tree.</summary>
    public static string ClientWebRoot { get; } = Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "UI", "Client", "wwwroot"));

    private readonly WarningSink _warnings = new();

    /// <summary>The warnings the server has logged since it started, as rendered text.</summary>
    public IReadOnlyCollection<string> Warnings => _warnings.Messages;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseWebRoot(ClientWebRoot);
        builder.UseSetting("ConnectionStrings:SqlConnectionString", ServerSqlConnectionString);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlConnectionString"] = ServerSqlConnectionString,
                ["AI_OpenAI_ApiKey"] = ServerOpenAiKey,
                ["AI_OpenAI_Url"] = "",
                ["AI_OpenAI_Model"] = "",
                ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = serverConnectionString,
                [BrowserTelemetry.ConnectionStringKey] = "",
                ["ApiKeyAuthentication:Enabled"] = "true",
                ["ApiKeyAuthentication:ValidationKey"] = ServerApiKey
            };
            if (samplingPercentage is not null)
            {
                settings[BrowserTelemetry.SamplingPercentageKey] = samplingPercentage;
            }

            config.AddInMemoryCollection(settings);
        });
        builder.ConfigureTestServices(services =>
        {
            // The host's own telemetry stays in the process whatever connection string a test gives it.
            services.Configure<TelemetryConfiguration>(telemetry => telemetry.DisableTelemetry = true);
            // The server's log pipeline (Serilog) writes to every sink the container holds.
            services.AddSingleton<ILogEventSink>(_warnings);
        });
    }

    private sealed class WarningSink : ILogEventSink
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages;

        public void Emit(LogEvent logEvent)
        {
            if (logEvent.Level == LogEventLevel.Warning)
            {
                _messages.Enqueue(logEvent.RenderMessage());
            }
        }
    }
}
