namespace ClearMeasure.Bootcamp.AcceptanceTests;

/// <summary>
/// Playwright tracing policy for acceptance tests, selected with the PLAYWRIGHT_TRACE environment variable.
/// </summary>
public enum TraceMode
{
    /// <summary>No tracing.</summary>
    Off,

    /// <summary>Trace every test and save every trace (PLAYWRIGHT_TRACE=on).</summary>
    On,

    /// <summary>Trace only retry attempts and save the trace when that attempt fails (PLAYWRIGHT_TRACE=on-first-retry).</summary>
    OnFirstRetry,

    /// <summary>Trace every test but save only failing tests' traces (PLAYWRIGHT_TRACE=retain-on-failure). Default.</summary>
    RetainOnFailure
}

/// <summary>
/// Resolves the <see cref="TraceMode"/> from the environment.
/// </summary>
public static class TraceModeSettings
{
    /// <summary>
    /// The effective tracing mode for this test run.
    /// </summary>
    public static TraceMode Current { get; } = Parse(Environment.GetEnvironmentVariable("PLAYWRIGHT_TRACE"));

    /// <summary>
    /// Parses a PLAYWRIGHT_TRACE value; unknown or empty values yield <see cref="TraceMode.RetainOnFailure"/>.
    /// </summary>
    public static TraceMode Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "off" or "0" or "false" => TraceMode.Off,
        "on" or "1" or "true" => TraceMode.On,
        "on-first-retry" => TraceMode.OnFirstRetry,
        _ => TraceMode.RetainOnFailure
    };
}
