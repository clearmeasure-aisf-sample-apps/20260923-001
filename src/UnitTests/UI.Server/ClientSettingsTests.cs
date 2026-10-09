using System.Text;
using ClearMeasure.Bootcamp.UI.Client;
using ClearMeasure.Bootcamp.UI.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
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

    [TestCase("0", 0)]
    [TestCase("25", 25)]
    [TestCase("100", 100)]
    [TestCase("12.5", 12.5)]
    [TestCase("0.1", 0.1)]
    [TestCase(" 50 ", 50)]
    [TestCase("050", 50)]
    [TestCase("0.00001", 0.00001)]
    [TestCase("1e1", 10)]
    public void BrowserSamplingPercentage_ShouldBeTheConfiguredNumber_WhenItIsFrom0To100(string value, double expected)
    {
        var logger = new StubLogger();
        var configuration = Configuration((BrowserTelemetry.SamplingPercentageKey, value));

        ClientSettings.BrowserSamplingPercentage(configuration, logger).ShouldBe(expected);

        logger.Entries.ShouldBeEmpty();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void BrowserSamplingPercentage_ShouldBeNullWithoutAWarning_WhenTheSettingIsNotThere(string? value)
    {
        var logger = new StubLogger();
        var configuration = Configuration(
            (BrowserTelemetry.SamplingPercentageKey, value),
            (EnvironmentVariableKey, RealConnectionString));

        ClientSettings.BrowserSamplingPercentage(configuration, logger).ShouldBeNull();

        logger.Entries.ShouldBeEmpty();
    }

    [TestCase("-1")]
    [TestCase("-0")]
    [TestCase("+25")]
    [TestCase("100.1")]
    [TestCase("101")]
    [TestCase("1000")]
    [TestCase("abc")]
    [TestCase("25%")]
    [TestCase("25 percent")]
    [TestCase("2 5")]
    [TestCase("1,5")]
    [TestCase("1e3")]
    [TestCase("0x10")]
    [TestCase("NaN")]
    [TestCase("Infinity")]
    [TestCase("true")]
    public void BrowserSamplingPercentage_ShouldBeNullWithOneWarning_WhenTheValueIsNotANumberFrom0To100(string value)
    {
        var logger = new StubLogger();
        var configuration = Configuration((BrowserTelemetry.SamplingPercentageKey, value));

        ClientSettings.BrowserSamplingPercentage(configuration, logger).ShouldBeNull();

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain(BrowserTelemetry.SamplingPercentageKey);
        entry.Message.ShouldContain($" is {value}, ");
    }

    [Test]
    public void BrowserSamplingPercentage_ShouldReadOnlyItsOwnKey_WhenTheServerHoldsOtherNumbers()
    {
        var logger = new StubLogger();
        var configuration = Configuration(
            ("ApplicationInsights:SamplingPercentage", "10"),
            ("BrowserSamplingPercentage", "20"),
            ("SamplingPercentage", "30"));

        ClientSettings.BrowserSamplingPercentage(configuration, logger).ShouldBeNull();

        logger.Entries.ShouldBeEmpty();
    }

    [Test]
    public void Merge_ShouldBeExactlyWhatItWasBeforeThereWasASamplingPercentage_WhenNoneIsGiven()
    {
        const string staticSettings = """{ "ApiKeyAuthentication": { "ValidationKey": "" } }""";
        string[] expected =
        [
            "{",
            """  "ApiKeyAuthentication": {""",
            """    "ValidationKey": "" """.TrimEnd(),
            "  },",
            """  "ApplicationInsights": {""",
            $"""    "ConnectionString": "{RealConnectionString}" """.TrimEnd(),
            "  }",
            "}"
        ];

        var merged = ClientSettings.Merge(staticSettings, RealConnectionString);

        merged.ShouldBe(string.Join(Environment.NewLine, expected));
    }

    [TestCase(0, "0")]
    [TestCase(25, "25")]
    [TestCase(100, "100")]
    [TestCase(12.5, "12.5")]
    [TestCase(0.1, "0.1")]
    [TestCase(0.00001, "1E-05")]
    public void Merge_ShouldAddTheSamplingPercentageBesideTheConnectionString_WhenOneIsGiven(
        double samplingPercentage, string expected)
    {
        const string staticSettings = """{ "ApiKeyAuthentication": { "ValidationKey": "client-key" } }""";

        var merged = ClientSettings.Merge(staticSettings, RealConnectionString, samplingPercentage);

        var settings = Settings(merged);
        settings.ShouldBe(new Dictionary<string, string?>
        {
            ["ApiKeyAuthentication:ValidationKey"] = "client-key",
            [BrowserTelemetry.ConnectionStringKey] = RealConnectionString,
            [BrowserTelemetry.SamplingPercentageKey] = expected
        }, ignoreOrder: true);
        BrowserTelemetry.SamplingPercentage(settings[BrowserTelemetry.SamplingPercentageKey])
            .ShouldBe(samplingPercentage);
    }

    [TestCase("""{ "ApplicationInsights": { "BrowserSamplingPercentage": 75 } }""")]
    [TestCase("""{ "applicationinsights": { "browsersamplingpercentage": "0" } }""")]
    [TestCase("""{ "ApplicationInsights": { "BrowserSamplingPercentage": { "Nested": 1 } } }""")]
    public void Merge_ShouldReplaceTheSamplingPercentage_WhenStaticSettingsAlreadyNameTheKey(string staticSettings)
    {
        var merged = ClientSettings.Merge(staticSettings, RealConnectionString, 25);

        var settings = new Dictionary<string, string?>(Settings(merged), StringComparer.OrdinalIgnoreCase);
        settings.Count.ShouldBe(2);
        settings[BrowserTelemetry.ConnectionStringKey].ShouldBe(RealConnectionString);
        settings[BrowserTelemetry.SamplingPercentageKey].ShouldBe("25");
    }

    [Test]
    public void Merge_ShouldKeepTheSamplingPercentageOfTheStaticSettings_WhenNoneIsGiven()
    {
        const string staticSettings = """{ "ApplicationInsights": { "BrowserSamplingPercentage": 75 } }""";

        var merged = ClientSettings.Merge(staticSettings, RealConnectionString);

        Settings(merged).ShouldBe(new Dictionary<string, string?>
        {
            [BrowserTelemetry.ConnectionStringKey] = RealConnectionString,
            [BrowserTelemetry.SamplingPercentageKey] = "75"
        }, ignoreOrder: true);
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

    [TestCase(0, "0")]
    [TestCase(25, "25")]
    public async Task InvokeAsync_ShouldAnswerWithTheSamplingPercentage_WhenTheServerHasOne(
        double samplingPercentage, string expected)
    {
        const string staticSettings = """{ "ApiKeyAuthentication": { "ValidationKey": "" } }""";
        var context = Request("GET", ClientSettings.RequestPath);

        var passedOn = await InvokeAsync(context, new StubWebRoot(staticSettings), samplingPercentage);

        passedOn.ShouldBeFalse();
        Settings(Body(context)).ShouldBe(new Dictionary<string, string?>
        {
            ["ApiKeyAuthentication:ValidationKey"] = "",
            [BrowserTelemetry.ConnectionStringKey] = RealConnectionString,
            [BrowserTelemetry.SamplingPercentageKey] = expected
        }, ignoreOrder: true);
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

    private static async Task<bool> InvokeAsync(
        HttpContext context, IFileProvider webRoot, double? samplingPercentage = null)
    {
        var passedOn = false;
        await ClientSettingsPipeline.InvokeAsync(context, _ =>
        {
            passedOn = true;
            return Task.CompletedTask;
        }, webRoot, new BrowserTelemetrySettings(RealConnectionString, samplingPercentage));
        return passedOn;
    }

    private static DefaultHttpContext Request(string method, string path)
    {
        return new DefaultHttpContext
        {
            Request = { Method = method, Path = path },
            Response = { Body = new MemoryStream() }
        };
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

    private sealed class StubLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

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
