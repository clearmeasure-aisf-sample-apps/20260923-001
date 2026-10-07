using System.Text.Json;
using ClearMeasure.Bootcamp.UI.Server.BuildFacts;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.UI.Server.BuildFacts;

[TestFixture]
public class BuildFactsProviderTests
{
    private const string RunningVersion = "2.5.773+0123abc";

    private string _contentRoot = null!;

    [SetUp]
    public void CreateContentRoot()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), $"build-facts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);
    }

    [TearDown]
    public void DeleteContentRoot()
    {
        Directory.Delete(_contentRoot, true);
    }

    [Test]
    public void Should_AnswerTheAssemblyVersionAndNullsWithoutWarning_When_TheContentRootHasNoRecord()
    {
        var logger = new StubLogger();
        var provider = new BuildFactsProvider(_contentRoot, RunningVersion, logger);

        using var document = JsonDocument.Parse(provider.Json);

        document.RootElement.GetProperty("version").GetString().ShouldBe("2.5.773");
        document.RootElement.GetProperty("commit").ValueKind.ShouldBe(JsonValueKind.Null);
        document.RootElement.GetProperty("code").ValueKind.ShouldBe(JsonValueKind.Null);
        logger.Warnings.ShouldBeEmpty();
    }

    [Test]
    public void Should_AnswerTheRecordsFacts_When_TheReleaseWroteOne()
    {
        WriteRecord("""{ "version": "2.5.773", "commit": "0123abcd", "tests": { "unit": 1009 } }""");
        var logger = new StubLogger();
        var provider = new BuildFactsProvider(_contentRoot, RunningVersion, logger);

        using var document = JsonDocument.Parse(provider.Json);

        document.RootElement.GetProperty("version").GetString().ShouldBe("2.5.773");
        document.RootElement.GetProperty("commit").GetString().ShouldBe("0123abcd");
        document.RootElement.GetProperty("tests").GetProperty("unit").GetInt32().ShouldBe(1009);
        document.RootElement.GetProperty("coverage").ValueKind.ShouldBe(JsonValueKind.Null);
        logger.Warnings.ShouldBeEmpty();
    }

    [Test]
    public void Should_AnswerWithoutTheRecordAndWarn_When_ItDescribesAnotherVersion()
    {
        WriteRecord("""{ "version": "2.5.772", "commit": "0123abcd", "tests": { "unit": 1009 } }""");
        var logger = new StubLogger();
        var provider = new BuildFactsProvider(_contentRoot, RunningVersion, logger);

        using var document = JsonDocument.Parse(provider.Json);

        document.RootElement.GetProperty("version").GetString().ShouldBe("2.5.773");
        document.RootElement.GetProperty("commit").ValueKind.ShouldBe(JsonValueKind.Null);
        document.RootElement.GetProperty("tests").ValueKind.ShouldBe(JsonValueKind.Null);
        logger.Warnings.ShouldHaveSingleItem().ShouldContain("2.5.773");
    }

    [Test]
    public void Should_AnswerWithoutTheRecordAndWarn_When_ItIsNotAJsonObject()
    {
        WriteRecord("{ \"version\": ");
        var logger = new StubLogger();
        var provider = new BuildFactsProvider(_contentRoot, RunningVersion, logger);

        using var document = JsonDocument.Parse(provider.Json);

        document.RootElement.GetProperty("version").GetString().ShouldBe("2.5.773");
        logger.Warnings.ShouldHaveSingleItem().ShouldContain(BuildFactsProvider.FileName);
    }

    [Test]
    public void Should_AnswerWithoutTheRecordAndWarn_When_ItNamesAPropertyTwice()
    {
        WriteRecord("""{ "version": "2.5.773", "commit": "first", "commit": "second" }""");
        var logger = new StubLogger();
        var provider = new BuildFactsProvider(_contentRoot, RunningVersion, logger);

        using var document = JsonDocument.Parse(provider.Json);

        document.RootElement.GetProperty("version").GetString().ShouldBe("2.5.773");
        document.RootElement.GetProperty("commit").ValueKind.ShouldBe(JsonValueKind.Null);
        logger.Warnings.ShouldHaveSingleItem().ShouldContain(BuildFactsProvider.FileName);
    }

    [Test]
    public void Should_ReadTheRecordOnce_When_AskedTwice()
    {
        WriteRecord("""{ "version": "2.5.773", "commit": "first" }""");
        var provider = new BuildFactsProvider(_contentRoot, RunningVersion, new StubLogger());
        var first = provider.Json;

        WriteRecord("""{ "version": "2.5.773", "commit": "second" }""");

        provider.Json.ShouldBeSameAs(first);
        first.ShouldContain("first");
    }

    [Test]
    public void Should_NameTheVersionOfTheVersionEndpointsAssembly_When_AskedForTheRunningVersion()
    {
        var running = BuildFactsProvider.RunningVersion;

        running.ShouldNotBeNullOrWhiteSpace();
        BuildFactsDocument.ToVersion(running)!.ShouldMatch(@"^\d+\.\d+\.\d+");
    }

    private void WriteRecord(string json)
    {
        File.WriteAllText(Path.Combine(_contentRoot, BuildFactsProvider.FileName), json);
    }

    private sealed class StubLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
