using System.Text.Json;
using System.Text.Json.Nodes;
using ClearMeasure.Bootcamp.UI.Server.BuildFacts;
using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.UI.Server.BuildFacts;

[TestFixture]
public class BuildFactsDocumentTests
{
    private static readonly string[] Contract =
    [
        "version", "commit", "commitUrl", "builtAt", "buildUrl", "code", "tests", "coverage", "complexity", "crap",
        "analysis"
    ];

    [Test]
    public void Should_AnswerEveryPropertyOfTheContractInOrder_When_ThereIsNoRecord()
    {
        using var document = JsonDocument.Parse(BuildFactsDocument.Create(null, "2.5.773+0123abc"));

        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name).ShouldBe(Contract);
        root.GetProperty("version").GetString().ShouldBe("2.5.773");
        root.EnumerateObject().Skip(1).ShouldAllBe(property => property.Value.ValueKind == JsonValueKind.Null);
    }

    [Test]
    public void Should_AnswerNullVersion_When_NeitherRecordNorAssemblyNamesOne()
    {
        using var document = JsonDocument.Parse(BuildFactsDocument.Create(null, null));

        document.RootElement.GetProperty("version").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public void Should_AnswerTheRecordsFactsUnchanged_When_ThereIsARecord()
    {
        var record = BuildFactsDocument.Parse(
            """
            { "version": "2.5.773", "commit": "0123abcd", "builtAt": "2026-10-06T05:00:00Z",
              "code": { "linesOfCode": 1000, "files": 10, "languages": [ { "name": "C#", "lines": 900, "files": 8 } ] },
              "tests": { "unit": 12, "integration": 3, "acceptance": null },
              "crap": { "max": 5.5, "threshold": 6, "overThreshold": 0 } }
            """)!;

        using var document = JsonDocument.Parse(BuildFactsDocument.Create(record, "2.5.773+0123abc"));

        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name).ShouldBe(Contract);
        root.GetProperty("commit").GetString().ShouldBe("0123abcd");
        root.GetProperty("builtAt").GetString().ShouldBe("2026-10-06T05:00:00Z");
        root.GetProperty("code").GetProperty("languages")[0].GetProperty("name").GetString().ShouldBe("C#");
        root.GetProperty("tests").GetProperty("unit").GetInt32().ShouldBe(12);
        root.GetProperty("tests").GetProperty("acceptance").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("crap").GetProperty("max").GetDouble().ShouldBe(5.5);
        root.GetProperty("coverage").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("analysis").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public void Should_AnswerTheRunningVersion_When_TheRecordNamesNone()
    {
        var record = BuildFactsDocument.Parse("""{ "commit": "0123abcd" }""")!;

        using var document = JsonDocument.Parse(BuildFactsDocument.Create(record, "2.5.773+0123abc"));

        document.RootElement.GetProperty("version").GetString().ShouldBe("2.5.773");
        document.RootElement.GetProperty("commit").GetString().ShouldBe("0123abcd");
    }

    [Test]
    public void Should_KeepTheRecordsVersion_When_TheAssemblyNamesNone()
    {
        var record = BuildFactsDocument.Parse("""{ "version": "2.5.773" }""")!;

        using var document = JsonDocument.Parse(BuildFactsDocument.Create(record, null));

        document.RootElement.GetProperty("version").GetString().ShouldBe("2.5.773");
    }

    [Test]
    public void Should_AppendPropertiesBeyondTheContract_When_TheRecordHasThem()
    {
        var record = BuildFactsDocument.Parse("""{ "images": { "ui": "sha256:abc" }, "version": "2.5.773" }""")!;

        using var document = JsonDocument.Parse(BuildFactsDocument.Create(record, "2.5.773"));

        var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToList();
        names.Take(Contract.Length).ShouldBe(Contract);
        names.Last().ShouldBe("images");
        document.RootElement.GetProperty("images").GetProperty("ui").GetString().ShouldBe("sha256:abc");
    }

    [Test]
    public void Should_NotChangeTheRecord_When_CreatingTheAnswer()
    {
        var record = BuildFactsDocument.Parse("""{ "version": "2.5.773", "tests": { "unit": 12 } }""")!;
        var before = record.ToJsonString();

        BuildFactsDocument.Create(record, "2.5.773+0123abc").ShouldNotBeNullOrEmpty();

        record.ToJsonString().ShouldBe(before);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not json")]
    [TestCase("{ \"version\": ")]
    [TestCase("[]")]
    [TestCase("\"2.5.773\"")]
    [TestCase("null")]
    [TestCase("{ \"version\": \"2.5.773\", \"version\": \"2.5.774\" }")]
    public void Should_ReturnNull_When_ParsingAnythingButAJsonObject(string? json)
    {
        BuildFactsDocument.Parse(json).ShouldBeNull();
    }

    [TestCase("2.5.773", "2.5.773+0123abc", true)]
    [TestCase("2.5.773+other", "2.5.773+0123abc", true)]
    [TestCase("2.5.772", "2.5.773+0123abc", false)]
    [TestCase("2.5.773-ci.0123abc", "2.5.773", false)]
    [TestCase("2.5.773", null, true)]
    [TestCase("2.5.773", "", true)]
    public void Should_TellWhetherTheRecordIsOfTheRunningBuild_When_ComparingVersions(
        string recorded,
        string? informationalVersion,
        bool expected)
    {
        var record = new JsonObject { ["version"] = recorded };

        BuildFactsDocument.Describes(record, informationalVersion).ShouldBe(expected);
    }

    [Test]
    public void Should_AcceptTheRecord_When_ItNamesNoVersionOrNoText()
    {
        BuildFactsDocument.Describes(new JsonObject(), "2.5.773").ShouldBeTrue();
        BuildFactsDocument.Describes(new JsonObject { ["version"] = null }, "2.5.773").ShouldBeTrue();
        BuildFactsDocument.Describes(new JsonObject { ["version"] = 2 }, "2.5.773").ShouldBeTrue();
    }

    [TestCase("2.5.773+0123abc", "2.5.773")]
    [TestCase("2.5.773", "2.5.773")]
    [TestCase(" 1.0.0 ", "1.0.0")]
    [TestCase("+0123abc", null)]
    [TestCase("", null)]
    [TestCase(null, null)]
    public void Should_DropBuildMetadata_When_ReadingAVersion(string? informationalVersion, string? expected)
    {
        BuildFactsDocument.ToVersion(informationalVersion).ShouldBe(expected);
    }
}
