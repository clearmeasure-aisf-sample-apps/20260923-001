using System.Reflection;
using ClearMeasure.Bootcamp.UI.Api.Controllers;

namespace ClearMeasure.Bootcamp.UI.Server.BuildFacts;

/// <summary>
/// Holds the answer of <c>GET /_build</c> for the life of the process: the record the release build wrote into the
/// image (<c>build-facts.json</c> in <paramref name="contentRootPath"/>), otherwise the same shape with the version
/// of the running assembly and nulls (a local run, a test host, an image built without a record). The file is read
/// once, on the first request; no database and no other service is involved.
/// </summary>
/// <param name="contentRootPath">The directory that holds the record: the content root of the app.</param>
/// <param name="informationalVersion">The version of the running build, as its assembly states it.</param>
/// <param name="logger">Receives a warning when a record is there and cannot be used.</param>
internal sealed class BuildFactsProvider(string contentRootPath, string? informationalVersion, ILogger logger)
{
    /// <summary>Name of the file the release build writes into the published app.</summary>
    internal const string FileName = "build-facts.json";

    private readonly Lazy<string> _json = new(() =>
        Load(Path.Combine(contentRootPath, FileName), informationalVersion, logger));

    /// <summary>
    /// The version of the running build: the informational version of the assembly that also answers
    /// <c>/api/version</c>, so both endpoints state the same version.
    /// </summary>
    internal static string? RunningVersion =>
        typeof(VersionController).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    /// <summary>The answer, as JSON.</summary>
    internal string Json => _json.Value;

    /// <summary>
    /// Creates the provider of the running app: the record of its content root, the version of its assembly.
    /// </summary>
    internal static BuildFactsProvider Create(IServiceProvider services) =>
        new(
            services.GetRequiredService<IHostEnvironment>().ContentRootPath,
            RunningVersion,
            services.GetRequiredService<ILogger<BuildFactsProvider>>());

    private static string Load(string path, string? informationalVersion, ILogger logger)
    {
        if (!File.Exists(path))
        {
            return BuildFactsDocument.Create(null, informationalVersion);
        }

        var record = BuildFactsDocument.Parse(Read(path, logger));
        if (record is null)
        {
            logger.LogWarning("Build facts file {Path} is not a readable JSON object; answering without it", path);
            return BuildFactsDocument.Create(null, informationalVersion);
        }

        if (BuildFactsDocument.Describes(record, informationalVersion))
        {
            return BuildFactsDocument.Create(record, informationalVersion);
        }

        logger.LogWarning(
            "Build facts file {Path} describes another version than the running {Version}; answering without it",
            path,
            BuildFactsDocument.ToVersion(informationalVersion));
        return BuildFactsDocument.Create(null, informationalVersion);
    }

    private static string? Read(string path, ILogger logger)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Build facts file {Path} could not be read", path);
            return null;
        }
    }
}
