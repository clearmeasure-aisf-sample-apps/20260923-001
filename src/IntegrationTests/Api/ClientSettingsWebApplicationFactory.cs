using ClearMeasure.Bootcamp.UI.Client;
using ClearMeasure.Bootcamp.UI.Server;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClearMeasure.Bootcamp.IntegrationTests.Api;

/// <summary>
/// Hosts UI.Server over the client's own web root, as a published image has it, with a given Application Insights
/// connection string in the server's configuration, for the client settings (<c>/appsettings.json</c>) tests.
/// API-key authentication is on and the server holds values the browser must never receive.
/// </summary>
public sealed class ClientSettingsWebApplicationFactory(string serverConnectionString)
    : WebApplicationFactory<UiServerWebApplicationMarker>
{
    public const string ServerApiKey = "client-settings-server-only-api-key";
    public const string ServerSqlConnectionString = "Data Source=:memory:";
    public const string ServerOpenAiKey = "client-settings-server-only-openai-key";

    /// <summary>The client's static web root in the source tree.</summary>
    public static string ClientWebRoot { get; } = Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "UI", "Client", "wwwroot"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseWebRoot(ClientWebRoot);
        builder.UseSetting("ConnectionStrings:SqlConnectionString", ServerSqlConnectionString);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlConnectionString"] = ServerSqlConnectionString,
                ["AI_OpenAI_ApiKey"] = ServerOpenAiKey,
                ["AI_OpenAI_Url"] = "",
                ["AI_OpenAI_Model"] = "",
                ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = serverConnectionString,
                [BrowserTelemetry.ConnectionStringKey] = "",
                ["ApiKeyAuthentication:Enabled"] = "true",
                ["ApiKeyAuthentication:ValidationKey"] = ServerApiKey
            });
        });
        // The host's own telemetry stays in the process whatever connection string a test gives it.
        builder.ConfigureTestServices(services =>
            services.Configure<TelemetryConfiguration>(telemetry => telemetry.DisableTelemetry = true));
    }
}
