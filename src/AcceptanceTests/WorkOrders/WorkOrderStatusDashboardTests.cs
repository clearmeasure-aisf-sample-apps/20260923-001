using IndexPage = ClearMeasure.Bootcamp.UI.Shared.Pages.Index;

namespace ClearMeasure.Bootcamp.AcceptanceTests.WorkOrders;

[TestFixture]
public class WorkOrderStatusDashboardTests : AcceptanceTestBase
{
    [Test, Retry(2)]
    public async Task ShouldShowStatusCardsWhenAuthenticated()
    {
        await LoginAsCurrentUser();

        await Page.GotoAsync("/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var dashboard = Page.GetByTestId(nameof(IndexPage.Elements.StatusDashboard));
        await dashboard.WaitForAsync();
        await Expect(dashboard).ToBeVisibleAsync();

        foreach (var status in WorkOrderStatus.GetAllItems())
        {
            var countLocator = Page.GetByTestId(nameof(IndexPage.Elements.StatusCardCount) + status.Key);
            await countLocator.WaitForAsync();
            await Expect(countLocator).ToBeVisibleAsync();

            var countText = await countLocator.InnerTextAsync();
            countText.ShouldMatch(@"^\d+$");
        }
    }

    [Test, Retry(2)]
    public async Task ShouldShowOpenWorkOrderTotalWhenAuthenticated()
    {
        await LoginAsCurrentUser();

        await Page.GotoAsync("/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var summary = Page.GetByTestId(nameof(IndexPage.Elements.OpenWorkOrders));
        await Expect(summary).ToBeVisibleAsync();
        await Expect(summary).ToContainTextAsync("open work orders");

        var countText = await Page.GetByTestId(nameof(IndexPage.Elements.OpenWorkOrdersCount)).InnerTextAsync();
        countText.ShouldMatch(@"^\d+$");
    }
}
