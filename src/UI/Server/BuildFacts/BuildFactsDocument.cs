using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClearMeasure.Bootcamp.UI.Server.BuildFacts;

/// <summary>
/// Shapes the answer of <c>GET /_build</c>: always the same properties, in the same order, each one null when the
/// build could not tell. The content is the record the release build wrote (<c>build-facts.json</c>); the app adds
/// nothing but the version of the running assembly, and refuses a record that describes another version.
/// </summary>
internal static class BuildFactsDocument
{
    private const string VersionProperty = "version";

    // The contract with the health dashboard's "Code" card. A property the record has beyond these follows them,
    // unchanged.
    private static readonly string[] Properties =
    [
        VersionProperty,
        "commit",
        "commitUrl",
        "builtAt",
        "buildUrl",
        "code",
        "tests",
        "coverage",
        "complexity",
        "crap",
        "analysis"
    ];

    /// <summary>
    /// Returns the record as a JSON object, or null when <paramref name="json"/> is absent or is not one.
    /// </summary>
    internal static JsonObject? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(json) is not JsonObject record)
            {
                return null;
            }

            // A property name that occurs twice is only refused when the object is first read: read it here, so
            // that such a record is no record instead of an error on every request.
            _ = record.Count;
            return record;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns whether <paramref name="record"/> can be the record of the running build: it names no version, the
    /// running version is unknown, or both are the same once build metadata (<c>+...</c>) is set aside.
    /// </summary>
    internal static bool Describes(JsonObject record, string? informationalVersion)
    {
        var running = ToVersion(informationalVersion);
        var recorded = ToVersion(ReadText(record[VersionProperty]));
        return running is null || recorded is null || string.Equals(running, recorded, StringComparison.Ordinal);
    }

    /// <summary>
    /// Serializes the answer: the facts of <paramref name="record"/> when there is one, and as version the one of
    /// the running assembly (<paramref name="informationalVersion"/>, without its build metadata), so that the
    /// answer and the version endpoint never disagree. Without a running version the record's own version stays.
    /// </summary>
    internal static string Create(JsonObject? record, string? informationalVersion)
    {
        var document = new JsonObject();
        foreach (var name in Properties)
        {
            document[name] = record?[name]?.DeepClone();
        }

        var running = ToVersion(informationalVersion);
        if (running is not null)
        {
            document[VersionProperty] = running;
        }

        if (record is null)
        {
            return document.ToJsonString();
        }

        foreach (var (name, value) in record.Where(property => !Properties.Contains(property.Key)))
        {
            document[name] = value?.DeepClone();
        }

        return document.ToJsonString();
    }

    /// <summary>
    /// Returns the version without build metadata: <c>2.5.773+0123abc</c> (the SDK appends the commit to the
    /// informational version) becomes <c>2.5.773</c>. Null when nothing is left.
    /// </summary>
    internal static string? ToVersion(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return null;
        }

        var metadata = informationalVersion.IndexOf('+');
        var version = (metadata < 0 ? informationalVersion : informationalVersion[..metadata]).Trim();
        return version.Length == 0 ? null : version;
    }

    private static string? ReadText(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
