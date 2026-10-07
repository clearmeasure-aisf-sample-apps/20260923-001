using System.Net.Mime;
using System.Text;

namespace ClearMeasure.Bootcamp.UI.Server.BuildFacts;

/// <summary>
/// Registers the build facts and maps <c>GET /_build</c>, which the health dashboard reads from the browser to show
/// what each environment runs: version, commit, build run, size of the code, tests, coverage, complexity and
/// static analysis.
/// </summary>
internal static class BuildFactsEndpoint
{
    /// <summary>The route of the endpoint.</summary>
    internal const string Route = "/_build";

    // The answer changes only with a deployment, and the dashboard asks again when the version changes: a browser
    // must then not reuse the previous build's answer, so every read is revalidated.
    private const string CacheControl = "no-cache";

    /// <summary>
    /// Adds <see cref="BuildFactsProvider"/>, one for the life of the process.
    /// </summary>
    internal static void AddBuildFacts(IServiceCollection services)
    {
        services.AddSingleton(BuildFactsProvider.Create);
    }

    /// <summary>
    /// Maps <c>GET /_build</c>: anonymous and read-only. It is outside <c>/api</c>, so the API key does not guard
    /// it, and it carries no rate-limiting metadata; a cross-origin read is allowed by the same server-wide CORS
    /// policy that covers <c>/_healthcheck</c>, when one is configured.
    /// </summary>
    internal static void MapBuildFacts(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Route, WriteFacts).AllowAnonymous();
    }

    private static IResult WriteFacts(HttpContext context)
    {
        var facts = context.RequestServices.GetRequiredService<BuildFactsProvider>();
        context.Response.Headers.CacheControl = CacheControl;
        return Results.Text(facts.Json, MediaTypeNames.Application.Json, Encoding.UTF8);
    }
}
