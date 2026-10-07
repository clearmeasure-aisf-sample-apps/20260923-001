using System.Net;
using System.Text.Json;
using ClearMeasure.Bootcamp.UI.Server.BuildFacts;
using Shouldly;

namespace ClearMeasure.Bootcamp.IntegrationTests.Api;

[TestFixture]
public class BuildFactsEndpointIntegrationTests
{
    private const string Route = "/_build";

    private static readonly string[] Contract =
    [
        "version", "commit", "commitUrl", "builtAt", "buildUrl", "code", "tests", "coverage", "complexity", "crap",
        "analysis"
    ];

    private string _recordDirectory = null!;

    [SetUp]
    public void CreateRecordDirectory()
    {
        _recordDirectory = Path.Combine(Path.GetTempPath(), $"build-facts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_recordDirectory);
    }

    [TearDown]
    public void DeleteRecordDirectory()
    {
        Directory.Delete(_recordDirectory, true);
    }

    [Test]
    public async Task Should_AnswerTheContractWithTheRunningVersionAndNulls_When_NoRecordExists()
    {
        await using var factory = new BuildFactsWebApplicationFactory(_recordDirectory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Route);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name).ShouldBe(Contract);
        root.GetProperty("version").GetString().ShouldBe(RunningVersion());
        root.EnumerateObject().Skip(1).ShouldAllBe(property => property.Value.ValueKind == JsonValueKind.Null);
    }

    [Test]
    public async Task Should_AnswerTheRecordsFacts_When_TheReleaseWroteARecord()
    {
        await WriteRecordAsync(RunningVersion());
        await using var factory = new BuildFactsWebApplicationFactory(_recordDirectory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Route);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name).ShouldBe(Contract);
        root.GetProperty("version").GetString().ShouldBe(RunningVersion());
        root.GetProperty("commit").GetString().ShouldBe("0a1b2c3d4e5f60718293a4b5c6d7e8f901234567");
        root.GetProperty("commitUrl").GetString().ShouldEndWith("/commit/0a1b2c3d4e5f60718293a4b5c6d7e8f901234567");
        root.GetProperty("builtAt").GetString().ShouldBe("2026-10-06T05:00:00Z");
        root.GetProperty("buildUrl").GetString().ShouldBe("https://g.codefresh.io/build/abc");
        root.GetProperty("code").GetProperty("linesOfCode").GetInt64().ShouldBe(84210);
        root.GetProperty("code").GetProperty("languages")[0].GetProperty("name").GetString().ShouldBe("C#");
        root.GetProperty("tests").GetProperty("unit").GetInt32().ShouldBe(1009);
        root.GetProperty("tests").GetProperty("acceptance").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("coverage").GetProperty("linePercent").GetDouble().ShouldBe(81.2);
        root.GetProperty("complexity").GetProperty("max").GetInt32().ShouldBe(34);
        root.GetProperty("crap").GetProperty("threshold").GetInt32().ShouldBe(6);
        root.GetProperty("analysis").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Should_AnswerTheVersionOfTheVersionEndpoint_When_BothAreAsked()
    {
        await WriteRecordAsync(RunningVersion());
        await using var factory = new BuildFactsWebApplicationFactory(_recordDirectory);
        using var client = factory.CreateClient();

        using var build = JsonDocument.Parse(await client.GetStringAsync(Route));
        using var version = JsonDocument.Parse(await client.GetStringAsync("/api/v1.0/version"));

        var informationalVersion = version.RootElement.GetProperty("informationalVersion").GetString();
        build.RootElement.GetProperty("version").GetString()
            .ShouldBe(BuildFactsDocument.ToVersion(informationalVersion));
    }

    [Test]
    public async Task Should_AnswerWithoutTheRecord_When_ItDescribesAnotherVersion()
    {
        await WriteRecordAsync("0.0.1");
        await using var factory = new BuildFactsWebApplicationFactory(_recordDirectory);
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync(Route));

        document.RootElement.GetProperty("version").GetString().ShouldBe(RunningVersion());
        document.RootElement.GetProperty("commit").ValueKind.ShouldBe(JsonValueKind.Null);
        document.RootElement.GetProperty("tests").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Should_AllowTheConfiguredOriginAsTheHealthCheckDoes_When_ReadFromTheDashboard()
    {
        await using var factory = new BuildFactsWebApplicationFactory(_recordDirectory);
        using var client = factory.CreateClient();

        using var build = await GetFromOriginAsync(client, Route, BuildFactsWebApplicationFactory.DashboardOrigin);
        using var health = await GetFromOriginAsync(
            client,
            "/_healthcheck",
            BuildFactsWebApplicationFactory.DashboardOrigin);

        build.StatusCode.ShouldBe(HttpStatusCode.OK);
        build.Headers.GetValues("Access-Control-Allow-Origin")
            .ShouldBe([BuildFactsWebApplicationFactory.DashboardOrigin]);
        health.Headers.GetValues("Access-Control-Allow-Origin")
            .ShouldBe([BuildFactsWebApplicationFactory.DashboardOrigin]);
    }

    [Test]
    public async Task Should_NotAllowAnotherOriginAsTheHealthCheckDoesNot_When_ReadFromElsewhere()
    {
        await using var factory = new BuildFactsWebApplicationFactory(_recordDirectory);
        using var client = factory.CreateClient();

        using var build = await GetFromOriginAsync(client, Route, "https://other.example");
        using var health = await GetFromOriginAsync(client, "/_healthcheck", "https://other.example");

        build.StatusCode.ShouldBe(HttpStatusCode.OK);
        build.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        health.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Test]
    public async Task Should_AnswerEveryRead_When_PolledMoreOftenThanTheApiRateLimitAllows()
    {
        await using var factory = new BuildFactsWebApplicationFactory(_recordDirectory);
        using var client = factory.CreateClient();

        for (var read = 0; read < 10; read++)
        {
            using var response = await GetFromOriginAsync(
                client,
                Route,
                BuildFactsWebApplicationFactory.DashboardOrigin);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.Contains("X-RateLimit-Limit").ShouldBeFalse();
        }
    }

    private static string? RunningVersion() => BuildFactsDocument.ToVersion(BuildFactsProvider.RunningVersion);

    private static async Task<HttpResponseMessage> GetFromOriginAsync(HttpClient client, string route, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Add("Origin", origin);
        return await client.SendAsync(request);
    }

    private Task WriteRecordAsync(string? version)
    {
        var record = $$"""
            { "version": "{{version}}",
              "commit": "0a1b2c3d4e5f60718293a4b5c6d7e8f901234567",
              "commitUrl": "https://github.com/o/r/commit/0a1b2c3d4e5f60718293a4b5c6d7e8f901234567",
              "builtAt": "2026-10-06T05:00:00Z",
              "buildUrl": "https://g.codefresh.io/build/abc",
              "code": { "linesOfCode": 84210, "files": 1203,
                        "languages": [ { "name": "C#", "lines": 61234, "files": 800 } ] },
              "tests": { "unit": 1009, "integration": 240, "acceptance": null },
              "coverage": { "linePercent": 81.2, "branchPercent": 70.1 },
              "complexity": { "average": 1.9, "max": 34, "methods": 5210 },
              "crap": { "max": 5.8, "threshold": 6, "overThreshold": 0 },
              "analysis": null }
            """;
        return File.WriteAllTextAsync(Path.Combine(_recordDirectory, BuildFactsProvider.FileName), record);
    }
}
