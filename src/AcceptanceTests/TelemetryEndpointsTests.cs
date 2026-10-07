namespace ClearMeasure.Bootcamp.AcceptanceTests;

[TestFixture]
public class TelemetryEndpointsTests
{
    [TestCase("https://dc.services.visualstudio.com/v2/track")]
    [TestCase("https://southcentralus-3.in.applicationinsights.azure.com/v2/track")]
    [TestCase("https://DC.SERVICES.VISUALSTUDIO.COM/v2.1/track")]
    public void Pattern_ShouldMatch_WhenRequestGoesToAnIngestionEndpoint(string url)
    {
        TelemetryEndpoints.Pattern.IsMatch(url).ShouldBeTrue();
    }

    [TestCase("https://localhost:7174/api/status/environment")]
    [TestCase("https://js.monitor.azure.com/scripts/b/ai.3.gbl.min.js")]
    [TestCase("https://example.test/?next=https://dc.services.visualstudio.com/v2/track")]
    public void Pattern_ShouldNotMatch_WhenRequestGoesElsewhere(string url)
    {
        TelemetryEndpoints.Pattern.IsMatch(url).ShouldBeFalse();
    }
}
