using System.Net;
using System.Text.Json;
using ClearMeasure.Bootcamp.UI.Server.BuildFacts;
using ClearMeasure.Bootcamp.UnitTests.Api;
using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.UI.Server.BuildFacts;

[TestFixture]
public class BuildFactsEndpointWebTests
{
    [Test]
    public async Task Should_AnswerTheContractWithTheAssemblyVersion_When_NoRecordWasWritten()
    {
        await using var factory = new ApiVersioningRoutingWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(BuildFactsEndpoint.Route);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name).ShouldBe(
        [
            "version", "commit", "commitUrl", "builtAt", "buildUrl", "code", "tests", "coverage", "complexity",
            "crap", "analysis"
        ]);
        root.GetProperty("version").GetString()!.ShouldMatch(@"^\d+\.\d+\.\d+");
        root.GetProperty("version").GetString()!.ShouldNotContain("+");
        root.GetProperty("commit").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("analysis").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Should_AskTheBrowserToRevalidate_When_AnsweringTheBuildFacts()
    {
        await using var factory = new ApiVersioningRoutingWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(BuildFactsEndpoint.Route);

        response.Headers.CacheControl!.NoCache.ShouldBeTrue();
        response.Headers.CacheControl.Public.ShouldBeFalse();
    }

    [Test]
    public async Task Should_AnswerWithoutAKey_When_TheApiKeyIsRequired()
    {
        await using var factory = new ApiKeyProtectedWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(BuildFactsEndpoint.Route);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Should_NeverLimitTheRate_When_TheApiAllowsOneRequestAMinute()
    {
        await using var factory = new RateLimitedApiWebApplicationFactory();
        using var client = factory.CreateClient();

        for (var request = 0; request < 5; request++)
        {
            using var response = await client.GetAsync(BuildFactsEndpoint.Route);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.Contains("X-RateLimit-Limit").ShouldBeFalse();
        }
    }

    [Test]
    public async Task Should_NotAnswerTheFacts_When_TheRequestIsNotAGet()
    {
        await using var factory = new ApiVersioningRoutingWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(BuildFactsEndpoint.Route, null);

        response.Content.Headers.ContentType?.MediaType.ShouldNotBe("application/json");
    }
}
