using Bunit;
using ClearMeasure.Bootcamp.Core;
using ClearMeasure.Bootcamp.Core.Model;
using ClearMeasure.Bootcamp.UI.Shared.Authentication;
using ClearMeasure.Bootcamp.UI.Shared.Pages;
using ClearMeasure.Bootcamp.UI.Shared.Services;
using ClearMeasure.Bootcamp.UnitTests.UI.Client.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Palermo.BlazorMvc;
using Shouldly;

namespace ClearMeasure.Bootcamp.UnitTests.UI.Shared.Pages;

[TestFixture]
public class WorkOrderSearchResultsHeaderTests
{
    [TestCase(0, "Search Results (0 work orders)")]
    [TestCase(1, "Search Results (1 work order)")]
    [TestCase(2, "Search Results (2 work orders)")]
    public async Task ShouldPluralizeResultsHeader_ByResultCount(int count, string expected)
    {
        var workOrders = Enumerable.Range(1, count).Select(i => new WorkOrder
        {
            Number = $"WO-{i}",
            Title = $"Order {i}",
            Status = WorkOrderStatus.Draft,
            Creator = new Employee("jpalermo", "Jeffrey", "Palermo", "jeffrey@example.com")
        }).ToArray();
        await using var ctx = CreateContext(new StubBus(workOrders));

        var component = ctx.Render<WorkOrderSearch>();

        component.Find($"#{WorkOrderSearch.Elements.ResultsHeader}").TextContent.Trim().ShouldBe(expected);
    }

    private static BunitContext CreateContext(IBus bus)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(bus);
        ctx.Services.AddSingleton<IUiBus>(new StubUiBus());
        ctx.Services.AddSingleton(TimeProvider.System);
        ctx.Services.AddSingleton(new WorkOrderSearchState());

        var store = new StubUserSessionStore { Username = "jpalermo" };
        var authProvider = new CustomAuthenticationStateProvider(store);
        authProvider.Login("jpalermo").GetAwaiter().GetResult();
        ctx.Services.AddSingleton<AuthenticationStateProvider>(authProvider);

        return ctx;
    }
}
