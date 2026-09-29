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
public class WorkOrderSearchAssignedColumnTests
{
    [Test]
    public async Task ShouldShowAssignedColumnHeaderAfterCreated()
    {
        await using var ctx = CreateContext(new StubBus());

        var component = ctx.Render<WorkOrderSearch>();

        var headers = component.FindAll(".grid-data thead th").Select(h => h.TextContent.Trim()).ToArray();
        var createdIndex = Array.IndexOf(headers, "Created");
        headers[createdIndex + 1].ShouldBe("Assigned");
    }

    [Test]
    public async Task ShouldShowFormattedAssignedDate_WhenAssignedDateIsSet()
    {
        var workOrder = NewWorkOrder("WO-ASG", new DateTime(2026, 4, 2, 10, 15, 0));
        await using var ctx = CreateContext(new StubBus([workOrder]));

        var component = ctx.Render<WorkOrderSearch>();

        var cell = component.Find($"[data-testid='{WorkOrderSearch.Elements.AssignedDateCell}WO-ASG']");
        cell.TextContent.Trim().ShouldBe("Apr 2, 2026");
    }

    [Test]
    public async Task ShouldShowBlankAssignedCell_WhenAssignedDateIsNull()
    {
        var workOrder = NewWorkOrder("WO-UNA", null);
        await using var ctx = CreateContext(new StubBus([workOrder]));

        var component = ctx.Render<WorkOrderSearch>();

        var cell = component.Find($"[data-testid='{WorkOrderSearch.Elements.AssignedDateCell}WO-UNA']");
        cell.TextContent.Trim().ShouldBeEmpty();
    }

    private static WorkOrder NewWorkOrder(string number, DateTime? assignedDate)
    {
        return new WorkOrder
        {
            Number = number,
            Title = "Assigned column",
            Status = WorkOrderStatus.Draft,
            Creator = new Employee("jpalermo", "Jeffrey", "Palermo", "jeffrey@example.com"),
            AssignedDate = assignedDate
        };
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
