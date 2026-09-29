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
public class WorkOrderSearchCreatedColumnTests
{
    [Test]
    public async Task ShouldShowCreatedColumnHeader()
    {
        await using var ctx = CreateContext(new StubBus());

        var component = ctx.Render<WorkOrderSearch>();

        var headers = component.FindAll(".grid-data thead th").Select(h => h.TextContent.Trim()).ToArray();
        headers.Last().ShouldBe("Created");
    }

    [Test]
    public async Task ShouldShowFormattedCreatedDate_WhenCreatedDateIsSet()
    {
        var workOrder = new WorkOrder
        {
            Number = "WO-CRT",
            Title = "Created column",
            Status = WorkOrderStatus.Draft,
            Creator = new Employee("jpalermo", "Jeffrey", "Palermo", "jeffrey@example.com"),
            CreatedDate = new DateTime(2026, 3, 7, 14, 30, 0)
        };
        await using var ctx = CreateContext(new StubBus([workOrder]));

        var component = ctx.Render<WorkOrderSearch>();

        var cell = component.Find($"[data-testid='{WorkOrderSearch.Elements.CreatedDateCell}WO-CRT']");
        cell.TextContent.Trim().ShouldBe("Mar 7, 2026");
    }

    [Test]
    public async Task ShouldShowBlankCreatedCell_WhenCreatedDateIsNull()
    {
        var workOrder = new WorkOrder
        {
            Number = "WO-NOD",
            Title = "No created date",
            Status = WorkOrderStatus.Draft,
            Creator = new Employee("jpalermo", "Jeffrey", "Palermo", "jeffrey@example.com"),
            CreatedDate = null
        };
        await using var ctx = CreateContext(new StubBus([workOrder]));

        var component = ctx.Render<WorkOrderSearch>();

        var cell = component.Find($"[data-testid='{WorkOrderSearch.Elements.CreatedDateCell}WO-NOD']");
        cell.TextContent.Trim().ShouldBeEmpty();
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
