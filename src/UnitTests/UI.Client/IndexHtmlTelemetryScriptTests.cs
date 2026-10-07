using ClearMeasure.Bootcamp.UI.Client;
using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.UI.Client;

[TestFixture]
public class IndexHtmlTelemetryScriptTests
{
    private string _markup = string.Empty;

    [SetUp]
    public void SetUp()
    {
        var indexHtml = Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "UI", "Client", "wwwroot", "index.html"));

        File.Exists(indexHtml).ShouldBeTrue();
        _markup = File.ReadAllText(indexHtml);
    }

    [Test]
    public void IndexHtml_TelemetryScript_ShouldDefineTheStartFunctionTheClientCalls()
    {
        _markup.ShouldContain($"window.{BrowserTelemetry.StartFunction} = function (connectionString) {{");
    }

    [Test]
    public void IndexHtml_TelemetryScript_ShouldNotStartWithoutAConnectionString()
    {
        _markup.ShouldContain("if (!connectionString || window.appInsights) { return; }");
    }

    [Test]
    public void IndexHtml_TelemetryScript_ShouldNotCarryAPlaceholderInstrumentationKey()
    {
        _markup.ShouldNotContain("00000000-0000-0000-0000-000000000000");
        _markup.ShouldNotContain("instrumentationKey:");
    }

    [Test]
    public void IndexHtml_TelemetryScript_ShouldReferenceTheSdkHostOnlyInsideTheStartFunction()
    {
        var startFunction = _markup.IndexOf($"window.{BrowserTelemetry.StartFunction} = function", StringComparison.Ordinal);
        startFunction.ShouldBeGreaterThan(-1);
        var scriptEnd = _markup.IndexOf("</script>", startFunction, StringComparison.Ordinal);

        var sdkSource = _markup.IndexOf("src: \"https://js.monitor.azure.com/", StringComparison.Ordinal);

        sdkSource.ShouldBeInRange(startFunction, scriptEnd);
        _markup.ShouldNotContain("<script src=\"https://js.monitor.azure.com");
    }
}
