using System.Net;
using System.Text;
using ClearMeasure.Bootcamp.UI.Client;
using ClearMeasure.Bootcamp.UI.Server;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace ClearMeasure.Bootcamp.IntegrationTests.Api;

[TestFixture]
public class ClientSettingsEndpointIntegrationTests
{
    // Endpoints on this machine that nothing listens on: no telemetry can leave a test.
    private const string LocalEndpoints = ";IngestionEndpoint=https://localhost:1/;LiveEndpoint=https://localhost:1/";
    private const string RealConnectionString = "InstrumentationKey=3f2c9a51-7d1e-4b8a-9c64-2a5e8f0b1d37" + LocalEndpoints;
    private const string PlaceholderConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000" + LocalEndpoints;

    private static string StaticSettingsFile => Path.Combine(
        ClientSettingsWebApplicationFactory.ClientWebRoot, ClientSettings.FileName);

    [Test]
    public async Task Should_AddTheServersConnectionStringToTheClientSettings_When_ServerHasARealConnectionString()
    {
        await using var factory = new ClientSettingsWebApplicationFactory(RealConnectionString);
        using var client = factory.CreateClient();
        var expected = Settings(await File.ReadAllTextAsync(StaticSettingsFile));
        expected[BrowserTelemetry.ConnectionStringKey] = RealConnectionString;

        var response = await client.GetAsync(ClientSettings.RequestPath);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        var settings = Settings(await response.Content.ReadAsStringAsync());
        settings.ShouldBe(expected, ignoreOrder: true);
        BrowserTelemetry.IsConfigured(settings[BrowserTelemetry.ConnectionStringKey]).ShouldBeTrue();
    }

    [Test]
    public async Task Should_ServeNoOtherServerConfiguration_When_ServerHasARealConnectionString()
    {
        await using var factory = new ClientSettingsWebApplicationFactory(RealConnectionString);
        using var client = factory.CreateClient();

        var body = await client.GetStringAsync(ClientSettings.RequestPath);

        body.ShouldNotContain(ClientSettingsWebApplicationFactory.ServerApiKey);
        body.ShouldNotContain(ClientSettingsWebApplicationFactory.ServerOpenAiKey);
        body.ShouldNotContain(ClientSettingsWebApplicationFactory.ServerSqlConnectionString);
        Settings(body)["ApiKeyAuthentication:ValidationKey"].ShouldBe(string.Empty);
    }

    [Test]
    public async Task Should_TellTheBrowserToAskAgainBeforeReusingTheSettings_When_ServerHasARealConnectionString()
    {
        await using var factory = new ClientSettingsWebApplicationFactory(RealConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(ClientSettings.RequestPath);

        response.Headers.CacheControl.ShouldNotBeNull().NoCache.ShouldBeTrue();
    }

    [Test]
    public async Task Should_AnswerHeadWithoutABody_When_ServerHasARealConnectionString()
    {
        await using var factory = new ClientSettingsWebApplicationFactory(RealConnectionString);
        using var client = factory.CreateClient();
        var expectedLength = (await client.GetByteArrayAsync(ClientSettings.RequestPath)).Length;

        using var request = new HttpRequestMessage(HttpMethod.Head, ClientSettings.RequestPath);
        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentLength.ShouldBe(expectedLength);
        (await response.Content.ReadAsByteArrayAsync()).ShouldBeEmpty();
    }

    [TestCase("")]
    [TestCase(PlaceholderConnectionString)]
    [TestCase("InstrumentationKey=not-a-key" + LocalEndpoints)]
    public async Task Should_ServeTheStaticClientSettingsUnchanged_When_ServerHasNoRealConnectionString(
        string serverConnectionString)
    {
        await using var factory = new ClientSettingsWebApplicationFactory(serverConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(ClientSettings.RequestPath);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).ShouldBe(await File.ReadAllBytesAsync(StaticSettingsFile));
        response.Headers.ETag.ShouldNotBeNull();
        response.Headers.CacheControl.ShouldBeNull();
        Settings(await response.Content.ReadAsStringAsync()).ShouldNotContainKey(BrowserTelemetry.ConnectionStringKey);
    }

    [Test]
    public async Task Should_KeepServingOtherStaticFilesAsTheyAre_When_ServerHasARealConnectionString()
    {
        await using var factory = new ClientSettingsWebApplicationFactory(RealConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/index.html");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync())
            .ShouldBe(await File.ReadAllBytesAsync(Path.Combine(ClientSettingsWebApplicationFactory.ClientWebRoot, "index.html")));
        response.Headers.CacheControl.ShouldBeNull();
    }

    // Reads settings the way the client does: through the JSON configuration provider.
    private static Dictionary<string, string?> Settings(string json) =>
        new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build()
            .AsEnumerable()
            .Where(setting => setting.Value is not null)
            .ToDictionary(setting => setting.Key, setting => setting.Value);
}
