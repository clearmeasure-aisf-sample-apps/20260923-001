using ChurchBulletin.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.ServiceDefaults;

[TestFixture]
public class RotatingJsonlFileWriterTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "rotating-jsonl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Test]
    public async Task WriteLine_WhenFileExceedsMaxSize_ShouldRotateToNewFile()
    {
        await using (var writer = new RotatingJsonlFileWriter(_directory, "metrics", "2026-01-01", 100, 10_000))
        {
            for (var i = 0; i < 10; i++)
            {
                writer.WriteLine(new string('x', 40));
            }
        }

        var files = Directory.GetFiles(_directory, "metrics_*.jsonl");
        files.Length.ShouldBeGreaterThan(1);
        files.ShouldAllBe(f => new FileInfo(f).Length <= 100);
        File.Exists(Path.Combine(_directory, "metrics_2026-01-01_001.jsonl")).ShouldBeTrue();
    }

    [Test]
    public async Task WriteLine_WhenTotalExceedsCap_ShouldDeleteOldestFilesAndStayUnderCap()
    {
        await using (var writer = new RotatingJsonlFileWriter(_directory, "metrics", "2026-01-01", 100, 300))
        {
            for (var i = 0; i < 50; i++)
            {
                writer.WriteLine(new string('x', 40));
            }
        }

        var files = new DirectoryInfo(_directory).GetFiles("*.jsonl");
        files.Sum(f => f.Length).ShouldBeLessThanOrEqualTo(300);
        files.ShouldNotContain(f => f.Name == "metrics_2026-01-01.jsonl");
    }

    [Test]
    public async Task Constructor_WhenTodaysFileAlreadyFull_ShouldStartAtNextIndex()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "logs_2026-01-01.jsonl"), new string('x', 200));

        await using var writer = new RotatingJsonlFileWriter(_directory, "logs", "2026-01-01", 100, 10_000);

        Path.GetFileName(writer.CurrentPath).ShouldBe("logs_2026-01-01_001.jsonl");
    }

    [Test]
    public void EnforceTotalSizeCap_WhenOverCap_ShouldKeepProtectedFile()
    {
        var keep = Path.Combine(_directory, "a.jsonl");
        var old = Path.Combine(_directory, "b.jsonl");
        File.WriteAllText(keep, new string('x', 100));
        File.WriteAllText(old, new string('x', 100));
        File.SetLastWriteTimeUtc(keep, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-1));

        TelemetryFileMaintenance.EnforceTotalSizeCap(_directory, 150, keep);

        File.Exists(keep).ShouldBeTrue();
        File.Exists(old).ShouldBeFalse();
    }

    [Test]
    public void FromConfiguration_WhenValuesMissing_ShouldUseDefaults()
    {
        var options = LocalTelemetryOptions.FromConfiguration(new ConfigurationBuilder().Build());

        options.Enabled.ShouldBeTrue();
        options.MaxFileSizeBytes.ShouldBe(LocalTelemetryOptions.DefaultMaxFileSizeBytes);
        options.MaxTotalSizeBytes.ShouldBe(LocalTelemetryOptions.DefaultMaxTotalSizeBytes);
    }

    [Test]
    public void FromConfiguration_WhenValuesConfigured_ShouldReadThem()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [LocalTelemetryOptions.EnabledKey] = "false",
                [LocalTelemetryOptions.MaxFileSizeBytesKey] = "1024",
                [LocalTelemetryOptions.MaxTotalSizeBytesKey] = "4096"
            })
            .Build();

        var options = LocalTelemetryOptions.FromConfiguration(configuration);

        options.Enabled.ShouldBeFalse();
        options.MaxFileSizeBytes.ShouldBe(1024);
        options.MaxTotalSizeBytes.ShouldBe(4096);
    }
}
