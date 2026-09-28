using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace ChurchBulletin.ServiceDefaults;

/// <summary>
/// Settings for <see cref="LocalTelemetryFileWriter"/>, read from the <c>LocalTelemetry</c> configuration section.
/// </summary>
public sealed record LocalTelemetryOptions
{
    /// <summary>Configuration key that enables or disables the local telemetry file writer.</summary>
    public const string EnabledKey = "LocalTelemetry:Enabled";

    /// <summary>Configuration key for the maximum size of a single JSONL file in bytes.</summary>
    public const string MaxFileSizeBytesKey = "LocalTelemetry:MaxFileSizeBytes";

    /// <summary>Configuration key for the maximum total size of all JSONL files in the folder in bytes.</summary>
    public const string MaxTotalSizeBytesKey = "LocalTelemetry:MaxTotalSizeBytes";

    /// <summary>Default maximum size of a single file (50 MB).</summary>
    public const long DefaultMaxFileSizeBytes = 50L * 1024 * 1024;

    /// <summary>Default maximum total size of the telemetry folder (200 MB).</summary>
    public const long DefaultMaxTotalSizeBytes = 200L * 1024 * 1024;

    /// <summary>Whether the file writer is registered. Defaults to <c>true</c>.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Size at which a file is rotated.</summary>
    public long MaxFileSizeBytes { get; init; } = DefaultMaxFileSizeBytes;

    /// <summary>Total folder size above which the oldest files are deleted.</summary>
    public long MaxTotalSizeBytes { get; init; } = DefaultMaxTotalSizeBytes;

    /// <summary>
    /// Reads options from configuration, falling back to defaults for missing or invalid values.
    /// </summary>
    public static LocalTelemetryOptions FromConfiguration(IConfiguration? configuration) =>
        new()
        {
            Enabled = !bool.TryParse(configuration?[EnabledKey], out var enabled) || enabled,
            MaxFileSizeBytes = ReadPositive(configuration?[MaxFileSizeBytesKey], DefaultMaxFileSizeBytes),
            MaxTotalSizeBytes = ReadPositive(configuration?[MaxTotalSizeBytesKey], DefaultMaxTotalSizeBytes)
        };

    private static long ReadPositive(string? raw, long fallback) =>
        long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : fallback;
}
