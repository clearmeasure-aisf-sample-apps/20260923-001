using System.Globalization;
using ClearMeasure.Bootcamp.UI.Shared;
using ClearMeasure.Bootcamp.UI.Shared.Pages;

namespace ClearMeasure.Bootcamp.AcceptanceTests.WorkOrders;

public class WorkOrderSearchCreatedColumnTests : AcceptanceTestBase
{
    [Test, Retry(2)]
    public async Task ShouldShowCreatedDateInSearchResults()
    {
        await LoginAsCurrentUser();
        var creator = CurrentUser;
        var order = Faker<WorkOrder>();
        order.Creator = creator;
        order.Title = $"[{TestTag}] created column";
        order.CreatedDate = new DateTime(2026, 3, 7, 9, 0, 0);
        await using var context = TestHost.NewDbContext();
        context.Attach(creator);
        context.Add(order);
        await context.SaveChangesAsync();

        await Click(nameof(NavMenu.Elements.MyWorkOrders));
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var cell = Page.GetByTestId(nameof(WorkOrderSearch.Elements.CreatedDateCell) + order.Number);
        await Expect(cell).ToHaveTextAsync(order.CreatedDate.Value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture));
    }
}
