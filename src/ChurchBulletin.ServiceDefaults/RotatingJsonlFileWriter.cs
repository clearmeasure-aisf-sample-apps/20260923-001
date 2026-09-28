using System.Globalization;
using System.Text;

namespace ChurchBulletin.ServiceDefaults;

/// <summary>
/// Thread-safe line writer that rotates to a new <c>{prefix}_{date}_{n}.jsonl</c> file when the current
/// file reaches the size limit. On each rotation the oldest JSONL files in the folder are deleted so that
/// closed files plus the active file stay within the total cap.
/// </summary>
internal sealed class RotatingJsonlFileWriter : IAsyncDisposable
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private readonly Lock _gate = new();
    private readonly string _directory;
    private readonly string _prefix;
    private readonly string _date;
    private readonly long _maxFileSizeBytes;
    private readonly long _maxTotalSizeBytes;
    private StreamWriter? _writer;
    private long _currentSize;
    private int _index;

    public RotatingJsonlFileWriter(string directory, string prefix, string date, long maxFileSizeBytes, long maxTotalSizeBytes)
    {
        _directory = directory;
        _prefix = prefix;
        _date = date;
        _maxFileSizeBytes = maxFileSizeBytes;
        _maxTotalSizeBytes = maxTotalSizeBytes;
        OpenNextFile();
    }

    /// <summary>Path of the file currently being written.</summary>
    public string CurrentPath { get; private set; } = string.Empty;

    public void WriteLine(string line)
    {
        lock (_gate)
        {
            if (_writer is null)
            {
                return;
            }

            var bytes = Utf8NoBom.GetByteCount(line) + Environment.NewLine.Length;
            if (_currentSize > 0 && _currentSize + bytes > _maxFileSizeBytes)
            {
                _writer.Dispose();
                _index++;
                OpenNextFile();
                TelemetryFileMaintenance.EnforceTotalSizeCap(
                    _directory, Math.Max(_maxTotalSizeBytes - _maxFileSizeBytes, 0), CurrentPath);
            }

            _writer.WriteLine(line);
            _currentSize += bytes;
        }
    }

    public async ValueTask DisposeAsync()
    {
        StreamWriter? writer;
        lock (_gate)
        {
            writer = _writer;
            _writer = null;
        }

        if (writer is not null)
        {
            await writer.DisposeAsync();
        }
    }

    private void OpenNextFile()
    {
        while (true)
        {
            var path = Path.Combine(_directory, BuildFileName());
            var existing = File.Exists(path) ? new FileInfo(path).Length : 0;
            if (existing < _maxFileSizeBytes)
            {
                CurrentPath = path;
                _currentSize = existing;
                _writer = new StreamWriter(path, append: true, Utf8NoBom) { AutoFlush = true };
                return;
            }

            _index++;
        }
    }

    private string BuildFileName() =>
        _index == 0
            ? $"{_prefix}_{_date}.jsonl"
            : string.Create(CultureInfo.InvariantCulture, $"{_prefix}_{_date}_{_index:D3}.jsonl");
}
