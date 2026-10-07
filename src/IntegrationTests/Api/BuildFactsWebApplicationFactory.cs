using ClearMeasure.Bootcamp.UI.Server;
using ClearMeasure.Bootcamp.UI.Server.BuildFacts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearMeasure.Bootcamp.IntegrationTests.Api;

/// <summary>
/// Hosts UI.Server for <c>/_build</c> integration tests as a deployed environment configures it: the API key
/// required, the API rate limit on, and CORS enabled for one dashboard origin. The build facts are read from
/// <paramref name="recordDirectory"/> instead of the content root, so a test decides whether a record exists.
/// </summary>
/// <param name="recordDirectory">The directory that holds <c>build-facts.json</c>, or holds none.</param>
public sealed class BuildFactsWebApplicationFactory(string recordDirectory)
    : WebApplicationFactory<UiServerWebApplicationMarker>
{
    /// <summary>The one origin the CORS policy allows.</summary>
    public const string DashboardOrigin = "https://dashboard.example";

    /// <summary>Validation key of the API key middleware.</summary>
    private const string IntegrationApiKey = "integration-test-api-key";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:SqlConnectionString", "Data Source=:memory:");
        builder.UseSetting("Cors:Enabled", "true");
        builder.UseSetting("Cors:AllowedOrigins:0", DashboardOrigin);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlConnectionString"] = "Data Source=:memory:",
                ["AI_OpenAI_ApiKey"] = "",
                ["AI_OpenAI_Url"] = "",
                ["AI_OpenAI_Model"] = "",
                ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "",
                ["ApiKeyAuthentication:Enabled"] = "true",
                ["ApiKeyAuthentication:ValidationKey"] = IntegrationApiKey,
                ["ApiRateLimiting:Enabled"] = "true",
                ["ApiRateLimiting:PermitLimit"] = "2",
                ["ApiRateLimiting:WindowSeconds"] = "60",
                ["ApiRateLimiting:SegmentsPerWindow"] = "2",
                ["ApiRateLimiting:QueueLimit"] = "0",
                ["Cors:Enabled"] = "true",
                ["Cors:AllowedOrigins:0"] = DashboardOrigin
            });
        });
        builder.ConfigureTestServices(services => services.AddSingleton(
            new BuildFactsProvider(recordDirectory, BuildFactsProvider.RunningVersion, NullLogger.Instance)));
    }
}
