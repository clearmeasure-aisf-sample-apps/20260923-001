namespace ClearMeasure.Bootcamp.AcceptanceTests.WorkOrders;

public class WorkOrderManagePageReloadTests : AcceptanceTestBase
{
    /// <summary>
    /// The first full page load of the manage page is kept from starting the app: its boot script is refused, so the
    /// page loads and never renders (the symptom of #68). The wait for that page runs its full 30 s before the reload.
    /// </summary>
    [Test]
    public async Task ShouldShowManagePage_WhenItsFirstFullPageLoadNeverRenders()
    {
        await LoginAsCurrentUser();
        var order = await CreateAndSaveNewWorkOrder();
        var bootScriptRequests = 0;
        await Page.RouteAsync("**/_framework/blazor.webassembly.js", route =>
            Interlocked.Increment(ref bootScriptRequests) == 1 ? route.AbortAsync() : route.FallbackAsync());

        await NavigateToManageEditAsync(order.Number!);

        bootScriptRequests.ShouldBe(2);
    }
}
