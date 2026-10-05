using ClearMeasure.Bootcamp.UI.Server;
using ClearMeasure.Bootcamp.UnitTests.UI.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.Api;

[TestFixture]
public class NServiceBusTestHostTests
{
    private static IEnumerable<TestCaseData> HttpTestHosts()
    {
        yield return Host(nameof(ApiKeyProtectedWebApplicationFactory), () => new ApiKeyProtectedWebApplicationFactory());
        yield return Host(nameof(ApiVersioningRoutingWebApplicationFactory), () => new ApiVersioningRoutingWebApplicationFactory());
        yield return Host(nameof(CorsEnabledApiWebApplicationFactory), () => new CorsEnabledApiWebApplicationFactory());
        yield return Host(nameof(IdempotencyMaxKeyWebApplicationFactory), () => new IdempotencyMaxKeyWebApplicationFactory());
        yield return Host(nameof(RequestBodyBufferingDisabledWebApplicationFactory), () => new RequestBodyBufferingDisabledWebApplicationFactory());
        yield return Host(nameof(WebServiceMessageValidationWebApplicationFactory), () => new WebServiceMessageValidationWebApplicationFactory());
    }

    [TestCaseSource(nameof(HttpTestHosts))]
    public async Task Should_RemoveNServiceBusHostedService_When_HostingHttpMiddlewareTests(
        Func<WebApplicationFactory<UiServerWebApplicationMarker>> createFactory)
    {
        await using var factory = createFactory();
        using var client = factory.CreateClient();

        factory.Services.GetServices<IHostedService>()
            .Select(service => service.GetType().FullName ?? string.Empty)
            .ShouldNotContain(name => name.StartsWith("NServiceBus", StringComparison.Ordinal));
    }

    private static TestCaseData Host(string name, Func<WebApplicationFactory<UiServerWebApplicationMarker>> createFactory) =>
        new TestCaseData(createFactory).SetArgDisplayNames(name);
}
