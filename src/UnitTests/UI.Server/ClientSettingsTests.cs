using System.Text;
using ClearMeasure.Bootcamp.UI.Client;
using ClearMeasure.Bootcamp.UI.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.UI.Server;

[TestFixture]
public class ClientSettingsTests
{
    private const string RealConnectionString =
        "InstrumentationKey=3f2c9a51-7d1e-4b8a-9c64-2a5e8f0b1d37;IngestionEndpoint=https://example.in.applicationinsights.azure.com/";

    private const string OtherRealConnectionString = "InstrumentationKey=8d1b6c2e-4f3a-4e7b-b0c9-5a6d7e8f9a10";
    private const string PlaceholderConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
    private const string EnvironmentVariableKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    [Test]
    public void ServerConnectionStringKeys_ShouldReadTheEnvironmentVariableBeforeTheKeyTheClientReads()
    {
        ClientSettings.ServerConnectionStringKeys
            .ShouldBe([EnvironmentVariableKey, BrowserTelemetry.ConnectionStringKey]);
    }

    [TestCase(EnvironmentVariableKey)]
    [TestCase(BrowserTelemetry.ConnectionStringKey)]
    public void BrowserConnectionString_ShouldBeTheServersConnectionString_WhenItHasARealInstrumentationKey(string key)
    {
        var configuration = Configuration((key, RealConnectionString));

        ClientSettings.BrowserConnectionString(configuration).ShouldBe(RealConnectionString);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(PlaceholderConnectionString)]
    [TestCase("InstrumentationKey=not-a-key")]
    [TestCase("IngestionEndpoint=https://example.in.applicationinsights.azure.com/")]
    public void BrowserConnectionString_ShouldBeNull_WhenTheServerHasNoRealInstrumentationKey(string? connectionString)
    {
        var configuration = Configuration((EnvironmentVariableKey, connectionString));

        ClientSettings.BrowserConnectionString(configuration).ShouldBeNull();
        BrowserTelemetry.IsConfigured(connectionString).ShouldBeFalse();
    }

    [Test]
    public void BrowserConnectionString_ShouldBeNull_WhenTheServerHasNoConnectionStringAtAll()
    {
        var configuration = Configuration(("ConnectionStrings:SqlConnectionString", "Data Source=:memory:"));

        ClientSettings.BrowserConnectionString(configuration).ShouldBeNull();
    }

    [Test]
    public void BrowserConnectionString_ShouldPreferTheEnvironmentVariable_WhenBothKeysHaveAValue()
    {
        var configuration = Configuration(
            (BrowserTelemetry.ConnectionStringKey, OtherRealConnectionString),
            (EnvironmentVariableKey, RealConnectionString));

        ClientSettings.BrowserConnectionString(configuration).ShouldBe(RealConnectionString);
    }

    [Test]
    public void BrowserConnectionString_ShouldBeNull_WhenTheEnvironmentVariableHoldsThePlaceholder()
    {
        var configuration = Configuration(
            (EnvironmentVariableKey, PlaceholderConnectionString),
            (BrowserTelemetry.ConnectionStringKey, RealConnectionString));

        ClientSettings.BrowserConnectionString(configuration).ShouldBeNull();
    }

    [TestCase("")]
    [TestCase("  ")]
    public void BrowserConnectionString_ShouldUseTheKeyTheClientReads_WhenTheEnvironmentVariableIsBlank(string blank)
    {
        var configuration = Configuration(
            (EnvironmentVariableKey, blank),
            (BrowserTelemetry.ConnectionStringKey, RealConnectionString));

        ClientSettings.BrowserConnectionString(configuration).ShouldBe(RealConnectionString);
    }

    [Test]
    public void Merge_ShouldAddTheConnectionStringUnderTheKeyTheClientReads_WhenStaticSettingsHoldOtherValues()
    {
        const string staticSettings = """{ "ApiKeyAuthentication": { "ValidationKey": "client-key" }, "Flag": true }""";

        var merged = ClientSettings.Merge(staticSettings, RealConnectionString);

        var settings = Settings(merged);
        settings.ShouldBe(new Dictionary<string, string?>
        {
            ["ApiKeyAuthentication:ValidationKey"] = "client-key",
            ["Flag"] = "True",
            [BrowserTelemetry.ConnectionStringKey] = RealConnectionString
        }, ignoreOrder: true);
        BrowserTelemetry.IsConfigured(settings[BrowserTelemetry.ConnectionStringKey]).ShouldBeTrue();
    }

    [Test]
    public void Merge_ShouldKeepWhatTheClientsSettingsFileHolds_WhenGivenTheFileTheClientShips()
    {
        var staticSettings = File.ReadAllText(ClientSettingsFile());

        var merged = ClientSettings.Merge(staticSettings, RealConnectionString);

        var expected = Settings(staticSettings);
        expected.ShouldNotContainKey(BrowserTelemetry.ConnectionStringKey);
        expected[BrowserTelemetry.ConnectionStringKey] = RealConnectionString;
        Settings(merged).ShouldBe(expected, ignoreOrder: true);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    [TestCase("{}")]
    public void Merge_ShouldHoldOnlyTheConnectionString_WhenThereAreNoStaticSettings(string? staticSettings)
    {
        var merged = ClientSettings.Merge(staticSettings, RealConnectionString);

        Settings(merged).ShouldBe(new Dictionary<string, string?>
        {
            [BrowserTelemetry.ConnectionStringKey] = RealConnectionString
        });
    }

    [TestCase("""{ "ApplicationInsights": { "ConnectionString": "InstrumentationKey=00000000-0000-0000-0000-000000000000" } }""")]
    [TestCase("""{ "applicationinsights": { "connectionstring": "" } }""")]
    [TestCase("""{ "APPLICATIONINSIGHTS": "not a section" }""")]
    [TestCase("""{ "ApplicationInsights": null }""")]
    public void Merge_ShouldReplaceTheValue_WhenStaticSettingsAlreadyNameTheKey(string staticSettings)
    {
        var merged = ClientSettings.Merge(staticSettings, RealConnectionString);

        var setting = Settings(merged).ShouldHaveSingleItem();
        setting.Key.ShouldBe(BrowserTelemetry.ConnectionStringKey, StringCompareShould.IgnoreCase);
        setting.Value.ShouldBe(RealConnectionString);
    }

    [Test]
    public void Merge_ShouldReadTheFileAsConfigurationDoes_WhenStaticSettingsHaveCommentsAndTrailingCommas()
    {
        const string staticSettings = """
                                      {
                                        // the client's own key
                                        "ApiKeyAuthentication": { "ValidationKey": "", },
                                      }
                                      """;

        var merged = ClientSettings.Merge(staticSettings, RealConnectionString);

        Settings(merged).ShouldBe(new Dictionary<string, string?>
        {
            ["ApiKeyAuthentication:ValidationKey"] = "",
            [BrowserTelemetry.ConnectionStringKey] = RealConnectionString
        }, ignoreOrder: true);
    }

    [TestCase("[1, 2]")]
    [TestCase("\"text\"")]
    [TestCase("null")]
    [TestCase("{ not json")]
    [TestCase("<!DOCTYPE html>")]
    [TestCase("""{ "ApplicationInsights": {}, "applicationinsights": {} }""")]
    public void Merge_ShouldBeNull_WhenStaticSettingsAreNotAJsonObjectConfigurationCanRead(string staticSettings)
    {
        ClientSettings.Merge(staticSettings, RealConnectionString).ShouldBeNull();
    }

    [TestCase("GET")]
    [TestCase("HEAD")]
    public async Task InvokeAsync_ShouldAnswerWithTheMergedSettings_WhenTheClientRequestsItsSettings(string method)
    {
        const string staticSettings = """{ "ApiKeyAuthentication": { "ValidationKey": "" } }""";
        var context = Request(method, ClientSettings.RequestPath);
        var expected = ClientSettings.Merge(staticSettings, RealConnectionString)!;

        var passedOn = await InvokeAsync(context, new StubWebRoot(staticSettings));

        passedOn.ShouldBeFalse();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        context.Response.ContentType.ShouldBe("application/json");
        context.Response.Headers.CacheControl.ToString().ShouldBe("no-cache");
        context.Response.ContentLength.ShouldBe(Encoding.UTF8.GetByteCount(expected));
        Body(context).ShouldBe(method == "GET" ? expected : string.Empty);
    }

    [Test]
    public async Task InvokeAsync_ShouldAnswerWithOnlyTheConnectionString_WhenThereIsNoStaticSettingsFile()
    {
        var context = Request("GET", ClientSettings.RequestPath);

        var passedOn = await InvokeAsync(context, new NullFileProvider());

        passedOn.ShouldBeFalse();
        Settings(Body(context)).ShouldBe(new Dictionary<string, string?>
        {
            [BrowserTelemetry.ConnectionStringKey] = RealConnectionString
        });
    }

    [TestCase("GET", "/index.html")]
    [TestCase("GET", "/appsettings.Development.json")]
    [TestCase("GET", "/api/appsettings.json")]
    [TestCase("GET", "/")]
    [TestCase("POST", "/appsettings.json")]
    [TestCase("PUT", "/appsettings.json")]
    public async Task InvokeAsync_ShouldPassTheRequestOn_WhenItIsNotAReadOfTheClientSettings(string method, string path)
    {
        var context = Request(method, path);

        var passedOn = await InvokeAsync(context, new StubWebRoot("{}"));

        passedOn.ShouldBeTrue();
        context.Response.Headers.ShouldBeEmpty();
        Body(context).ShouldBeEmpty();
    }

    [Test]
    public async Task InvokeAsync_ShouldPassTheRequestOn_WhenTheStaticSettingsAreNotAJsonObject()
    {
        var context = Request("GET", ClientSettings.RequestPath);

        var passedOn = await InvokeAsync(context, new StubWebRoot("<!DOCTYPE html>"));

        passedOn.ShouldBeTrue();
        context.Response.Headers.ShouldBeEmpty();
        Body(context).ShouldBeEmpty();
    }

    private static async Task<bool> InvokeAsync(HttpContext context, IFileProvider webRoot)
    {
        var passedOn = false;
        await ClientSettingsPipeline.InvokeAsync(context, _ =>
        {
            passedOn = true;
            return Task.CompletedTask;
        }, webRoot, RealConnectionString);
        return passedOn;
    }

    private static DefaultHttpContext Request(string method, string path)
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        context.Request.Method = method;
        context.Request.Path = path;
        return context;
    }

    private static string Body(HttpContext context) =>
        Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(value => value.Key, value => value.Value))
            .Build();

    // Reads settings the way the client does: through the JSON configuration provider.
    private static Dictionary<string, string?> Settings(string? json)
    {
        json.ShouldNotBeNull();
        return new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build()
            .AsEnumerable()
            .Where(setting => setting.Value is not null)
            .ToDictionary(setting => setting.Key, setting => setting.Value);
    }

    private static string ClientSettingsFile() =>
        Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "UI", "Client", "wwwroot", ClientSettings.FileName));

    private sealed class StubWebRoot(string settings) : IFileProvider
    {
        public IFileInfo GetFileInfo(string subpath) =>
            subpath == ClientSettings.FileName ? new StubFile(settings) : new NotFoundFileInfo(subpath);

        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    }

    private sealed class StubFile(string content) : IFileInfo
    {
        // With a byte-order mark, as an editor may save the file.
        private readonly byte[] _bytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(content)];

        public bool Exists => true;
        public bool IsDirectory => false;
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Length => _bytes.Length;
        public string Name => ClientSettings.FileName;
        public string? PhysicalPath => null;

        public Stream CreateReadStream() => new MemoryStream(_bytes);
    }
}
