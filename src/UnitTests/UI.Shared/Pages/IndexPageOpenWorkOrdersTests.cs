using Bunit;
using ClearMeasure.Bootcamp.Core;
using ClearMeasure.Bootcamp.Core.Model;
using ClearMeasure.Bootcamp.Core.Queries;
using ClearMeasure.Bootcamp.UI.Shared;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Palermo.BlazorMvc;
using Shouldly;
using IndexPage = ClearMeasure.Bootcamp.UI.Shared.Pages.Index;

namespace ClearMeasure.Bootcamp.UnitTests.UI.Shared.Pages;

[TestFixture]
public class IndexPageOpenWorkOrdersTests
{
    [Test]
    public async Task ShouldShowOpenCount_ExcludingCompleteAndCancelled()
    {
        var counts = new Dictionary<string, int>
        {
            [WorkOrderStatus.Draft.Key] = 2,
            [WorkOrderStatus.Assigned.Key] = 3,
            [WorkOrderStatus.InProgress.Key] = 4,
            [WorkOrderStatus.Complete.Key] = 10,
            [WorkOrderStatus.Cancelled.Key] = 20
        };
        await using var ctx = CreateAuthorizedContext(counts);

        var component = ctx.Render<IndexPage>();

        var count = component.Find($"[data-testid='{nameof(IndexPage.Elements.OpenWorkOrdersCount)}']");
        count.TextContent.Trim().ShouldBe("9");
    }

    [Test]
    public async Task ShouldShowZeroOpen_WhenAllWorkOrdersAreClosed()
    {
        var counts = new Dictionary<string, int>
        {
            [WorkOrderStatus.Complete.Key] = 5,
            [WorkOrderStatus.Cancelled.Key] = 1
        };
        await using var ctx = CreateAuthorizedContext(counts);

        var component = ctx.Render<IndexPage>();

        var summary = component.Find($"[data-testid='{nameof(IndexPage.Elements.OpenWorkOrders)}']");
        summary.TextContent.ShouldContain("open work orders");
        component.Find($"[data-testid='{nameof(IndexPage.Elements.OpenWorkOrdersCount)}']")
            .TextContent.Trim().ShouldBe("0");
    }

    [Test]
    public async Task ShouldNotShowOpenCount_WhenNotAuthenticated()
    {
        await using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IUiBus>(new StubUiBus());
        ctx.Services.AddSingleton<IBus>(new StubCountBus(new Dictionary<string, int>()));
        ctx.AddAuthorization();

        var component = ctx.Render<IndexPage>();

        component.FindAll($"[data-testid='{nameof(IndexPage.Elements.OpenWorkOrders)}']").ShouldBeEmpty();
    }

    private static BunitContext CreateAuthorizedContext(Dictionary<string, int> counts)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton<IUiBus>(new StubUiBus());
        ctx.Services.AddSingleton<IBus>(new StubCountBus(counts));
        ctx.AddAuthorization().SetAuthorized("jpalermo");
        return ctx;
    }

    private class StubCountBus(Dictionary<string, int> counts) : Bus(null!)
    {
        public override Task Publish(INotification notification) => Task.CompletedTask;

        public override Task<TResponse> Send<TResponse>(IRequest<TResponse> request)
        {
            if (request is WorkOrderCountByStatusQuery)
                return Task.FromResult((TResponse)(object)counts);

            throw new NotImplementedException($"Unhandled request type: {request.GetType().Name}");
        }
    }
}
