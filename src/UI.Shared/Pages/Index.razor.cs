using ClearMeasure.Bootcamp.Core.Model;
using ClearMeasure.Bootcamp.Core.Queries;
using Palermo.BlazorMvc;

namespace ClearMeasure.Bootcamp.UI.Shared.Pages;

public partial class Index : AppComponentBase, IListener<WorkOrderChangedEvent>
{
    private Dictionary<string, int> _statusCounts = new();
    private static readonly WorkOrderStatus[] StatusItems = WorkOrderStatus.GetAllItems();
    private static readonly string[] ClosedStatusKeys = [WorkOrderStatus.Complete.Key, WorkOrderStatus.Cancelled.Key];

    /// <summary>
    /// Total work orders that are neither complete nor cancelled.
    /// </summary>
    private int OpenWorkOrderCount =>
        _statusCounts.Where(pair => !ClosedStatusKeys.Contains(pair.Key)).Sum(pair => pair.Value);

    protected override async Task OnInitializedAsync()
    {
        await LoadCountsAsync();
    }

    private async Task LoadCountsAsync()
    {
        _statusCounts = await Bus.Send(new WorkOrderCountByStatusQuery());
        StateHasChanged();
    }

    public void Handle(WorkOrderChangedEvent theEvent)
    {
        InvokeAsync(LoadCountsAsync);
    }
}
