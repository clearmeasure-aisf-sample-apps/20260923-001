namespace ChurchBulletin.ServiceDefaults;

internal static class TelemetryFileMaintenance
{
    public static void DeleteFilesOlderThan(string directory, int retentionDays)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

            foreach (var file in Directory.GetFiles(directory, "*.jsonl"))
            {
                if (File.GetCreationTimeUtc(file) < cutoffDate)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// Deletes the oldest <c>*.jsonl</c> files (by last write time) until the folder total is at or below
    /// <paramref name="maxTotalBytes"/>. The file at <paramref name="keepPath"/> is never deleted.
    /// </summary>
    public static void EnforceTotalSizeCap(string directory, long maxTotalBytes, string? keepPath = null)
    {
        try
        {
            var files = new DirectoryInfo(directory).GetFiles("*.jsonl")
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();
            var total = files.Sum(f => f.Length);
            var keepFullPath = Path.GetFullPath(keepPath ?? string.Empty);

            foreach (var file in files.TakeWhile(_ => total > maxTotalBytes))
            {
                if (!string.Equals(file.FullName, keepFullPath, StringComparison.Ordinal))
                {
                    total -= TryDelete(file);
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    // Returns the bytes freed; a file open by another writer (Windows) is skipped.
    private static long TryDelete(FileInfo file)
    {
        try
        {
            var length = file.Length;
            file.Delete();
            return length;
        }
        catch
        {
            return 0;
        }
    }

    public static async ValueTask DisposeWritersAsync(params IAsyncDisposable?[] writers)
    {
        foreach (var writer in writers)
        {
            if (writer is null)
            {
                continue;
            }

            await writer.DisposeAsync();
        }
    }
}
