using System.Globalization;
using ClearMeasure.Bootcamp.UI.Shared;
using ClearMeasure.Bootcamp.UI.Shared.Pages;

namespace ClearMeasure.Bootcamp.AcceptanceTests.WorkOrders;

public class WorkOrderSearchAssignedColumnTests : AcceptanceTestBase
{
    [Test, Retry(2)]
    public async Task ShouldShowAssignedDateInSearchResults()
    {
        await LoginAsCurrentUser();
        var creator = CurrentUser;
        var order = Faker<WorkOrder>();
        order.Creator = creator;
        order.Assignee = creator;
        order.Title = $"[{TestTag}] assigned column";
        order.AssignedDate = new DateTime(2026, 4, 2, 9, 0, 0);
        await using var context = TestHost.NewDbContext();
        context.Attach(creator);
        context.Add(order);
        await context.SaveChangesAsync();

        await Click(nameof(NavMenu.Elements.MyWorkOrders));
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.Locator(".grid-data thead th", new() { HasTextString = "Assigned" })).ToBeVisibleAsync();
        var cell = Page.GetByTestId(nameof(WorkOrderSearch.Elements.AssignedDateCell) + order.Number);
        await Expect(cell).ToHaveTextAsync(order.AssignedDate.Value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture));
    }
}
