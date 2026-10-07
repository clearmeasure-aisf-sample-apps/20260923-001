using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClearMeasure.Bootcamp.UI.Client;
using Microsoft.Extensions.FileProviders;

namespace ClearMeasure.Bootcamp.UI.Server;

/// <summary>
/// Generates the settings the browser client reads at start-up (<c>/appsettings.json</c>) from the client's static
/// settings file plus one value from the server's configuration: the Application Insights connection string, and
/// only when it names a real resource. A browser telemetry connection string is visible to every visitor by design;
/// no other server configuration is ever served.
/// </summary>
public static class ClientSettings
{
    /// <summary>Path the browser client requests its settings from.</summary>
    public const string RequestPath = "/appsettings.json";

    /// <summary>Name of the client's static settings file in the web root.</summary>
    public const string FileName = "appsettings.json";

    private static readonly JsonNodeOptions CaseInsensitiveNames = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonDocumentOptions ConfigurationFileSyntax = new()
        { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// Server configuration keys that hold the Application Insights connection string, in the order the server's
    /// own telemetry reads them: the first one with a value is the server's connection string.
    /// </summary>
    public static IReadOnlyList<string> ServerConnectionStringKeys { get; } =
        ["APPLICATIONINSIGHTS_CONNECTION_STRING", BrowserTelemetry.ConnectionStringKey];

    /// <summary>
    /// The connection string the browser may use: the server's own, when it has a real instrumentation key
    /// (<see cref="BrowserTelemetry.IsConfigured"/>); otherwise <c>null</c>.
    /// </summary>
    /// <param name="configuration">The server's configuration.</param>
    public static string? BrowserConnectionString(IConfiguration configuration)
    {
        var connectionString = ServerConnectionStringKeys
            .Select(key => configuration[key])
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return BrowserTelemetry.IsConfigured(connectionString) ? connectionString : null;
    }

    /// <summary>
    /// Adds the connection string to the client's static settings under the key the client reads
    /// (<see cref="BrowserTelemetry.ConnectionStringKey"/>), keeping everything else the file contains.
    /// </summary>
    /// <param name="staticSettings">Content of the client's static settings file; <c>null</c> when there is none.</param>
    /// <param name="connectionString">The connection string the browser may use.</param>
    /// <returns>The merged settings, or <c>null</c> when the static settings are not a JSON object.</returns>
    public static string? Merge(string? staticSettings, string connectionString)
    {
        try
        {
            if (Parse(staticSettings) is not JsonObject settings)
            {
                return null;
            }

            var names = BrowserTelemetry.ConnectionStringKey.Split(ConfigurationPath.KeyDelimiter);
            names[..^1].Aggregate(settings, Section)[names[^1]] = connectionString;
            return settings.ToJsonString(Indented);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Serves the generated settings in place of the client's static settings file when the server has a connection
    /// string the browser may use. Without one nothing is added to the pipeline and the static file is served as is.
    /// Must be placed before the static file middleware.
    /// </summary>
    /// <param name="app">The server application.</param>
    public static void UseClientSettings(this WebApplication app)
    {
        if (BrowserConnectionString(app.Configuration) is { } connectionString)
        {
            app.UseMiddleware<ClientSettingsMiddleware>(connectionString);
        }
    }

    private static JsonNode? Parse(string? staticSettings) =>
        string.IsNullOrWhiteSpace(staticSettings)
            ? new JsonObject(CaseInsensitiveNames)
            : JsonNode.Parse(staticSettings, CaseInsensitiveNames, ConfigurationFileSyntax);

    private static JsonObject Section(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject existing)
        {
            return existing;
        }

        var section = new JsonObject(CaseInsensitiveNames);
        parent[name] = section;
        return section;
    }
}

internal sealed class ClientSettingsMiddleware(
    RequestDelegate next,
    IWebHostEnvironment environment,
    string connectionString)
{
    public Task InvokeAsync(HttpContext context) =>
        ClientSettingsPipeline.InvokeAsync(context, next, environment.WebRootFileProvider, connectionString);
}

internal static class ClientSettingsPipeline
{
    internal static async Task InvokeAsync(
        HttpContext context,
        RequestDelegate next,
        IFileProvider webRoot,
        string connectionString)
    {
        if (!IsSettingsRequest(context.Request)
            || ClientSettings.Merge(await ReadStaticSettingsAsync(webRoot), connectionString) is not { } settings)
        {
            await next(context);
            return;
        }

        // The settings change with the environment's configuration, not with the image: the browser may keep a copy
        // but has to ask again before using it, so a deployment's value never outlives the deployment.
        var body = Encoding.UTF8.GetBytes(settings);
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.ContentLength = body.Length;
        if (HttpMethods.IsGet(context.Request.Method))
        {
            await context.Response.Body.WriteAsync(body, context.RequestAborted);
        }
    }

    private static bool IsSettingsRequest(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        && request.Path.Equals(ClientSettings.RequestPath, StringComparison.OrdinalIgnoreCase);

    private static async Task<string?> ReadStaticSettingsAsync(IFileProvider webRoot)
    {
        var file = webRoot.GetFileInfo(ClientSettings.FileName);
        if (!file.Exists)
        {
            return null;
        }

        await using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
