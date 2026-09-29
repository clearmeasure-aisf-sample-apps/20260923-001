using Bunit;
using ClearMeasure.Bootcamp.Core;
using ClearMeasure.Bootcamp.Core.Model;
using ClearMeasure.Bootcamp.Core.Queries;
using ClearMeasure.Bootcamp.Core.Services;
using ClearMeasure.Bootcamp.UI.Shared;
using ClearMeasure.Bootcamp.UI.Shared.Pages;
using MediatR;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Palermo.BlazorMvc;
using Shouldly;
using Toolbelt.Blazor.Extensions.DependencyInjection;

namespace ClearMeasure.Bootcamp.UnitTests.UI.Shared.Pages;

[TestFixture]
public class WorkOrderManageTitleCharCountTests
{
    [Test]
    public async Task TitleCharCount_ShowsFullLimit_WhenTitleIsEmpty()
    {
        await using var ctx = CreateNewModeContext(title: null);

        var component = ctx.Render<WorkOrderManage>();

        var caption = await component.WaitForElementAsync(
            $"[data-testid='{WorkOrderManage.Elements.TitleCharCount}']");
        caption.TextContent.Trim().ShouldBe("300 characters remaining");
        caption.ClassList.ShouldContain("text-muted");
    }

    [Test]
    public async Task TitleCharCount_Decrements_AsTitleLengthGrows()
    {
        await using var ctx = CreateNewModeContext(title: new string('A', 10));

        var component = ctx.Render<WorkOrderManage>();

        var caption = await component.WaitForElementAsync(
            $"[data-testid='{WorkOrderManage.Elements.TitleCharCount}']");
        caption.TextContent.Trim().ShouldBe("290 characters remaining");
    }

    [Test]
    public async Task TitleCharCount_ShowsDanger_WhenAtLimit()
    {
        await using var ctx = CreateNewModeContext(title: new string('X', WorkOrder.TitleMaxLength));

        var component = ctx.Render<WorkOrderManage>();

        var caption = await component.WaitForElementAsync(
            $"[data-testid='{WorkOrderManage.Elements.TitleCharCount}']");
        caption.TextContent.Trim().ShouldBe("0 characters remaining");
        caption.ClassList.ShouldContain("text-danger");
    }

    [Test]
    public async Task TitleCharCount_NotRendered_WhenReadOnly()
    {
        await using var ctx = new BunitContext();

        var viewer = new Employee("viewer", "View", "Only", "viewer@example.com") { Id = Guid.NewGuid() };
        var creator = new Employee("creator", "Create", "Or", "creator@example.com") { Id = Guid.NewGuid() };
        var completedWorkOrder = new WorkOrder
        {
            Id = Guid.NewGuid(),
            Number = "WO-RO",
            Status = WorkOrderStatus.Complete,
            Creator = creator,
            Assignee = creator,
            Title = "Completed",
            Description = "Done"
        };

        ctx.Services.AddSingleton<IBus>(new StubWorkOrderManageBus(completedWorkOrder));
        ctx.Services.AddSingleton<IUiBus>(new StubUiBus());
        ctx.Services.AddSingleton(TimeProvider.System);
        ctx.Services.AddSingleton<IWorkOrderBuilder>(new StubWorkOrderBuilder(Guid.NewGuid(), creator, title: null));
        ctx.Services.AddSingleton<IUserSession>(new StubUserSession(viewer));
        ctx.Services.AddSingleton<ITranslationService>(new StubTranslationService());
        ctx.Services.AddSpeechSynthesis();
        ctx.Services.AddSpeechRecognition();

        var navigationManager = ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo(navigationManager.GetUriWithQueryParameter("Mode", "Edit"));

        var component = ctx.Render<WorkOrderManage>(parameters =>
            parameters.Add(p => p.Id, completedWorkOrder.Number));

        await component.WaitForElementAsync($"[data-testid='{WorkOrderManage.Elements.Title}']");

        var captions = component.FindAll($"[data-testid='{WorkOrderManage.Elements.TitleCharCount}']");
        captions.ShouldBeEmpty();
    }

    [Test]
    public async Task TitleInput_HasMaxLengthAttribute_EqualToTitleMaxLength()
    {
        await using var ctx = CreateNewModeContext(title: null);

        var component = ctx.Render<WorkOrderManage>();

        var input = await component.WaitForElementAsync(
            $"[data-testid='{WorkOrderManage.Elements.Title}']");
        input.GetAttribute("maxlength").ShouldBe(WorkOrder.TitleMaxLength.ToString());
    }

    private static BunitContext CreateNewModeContext(string? title)
    {
        var ctx = new BunitContext();
        var creator = new Employee("jpalermo", "Jeffrey", "Palermo", "jp@example.com") { Id = Guid.NewGuid() };
        var workOrderId = Guid.NewGuid();

        ctx.Services.AddSingleton<IBus>(new StubWorkOrderManageBus());
        ctx.Services.AddSingleton<IUiBus>(new StubUiBus());
        ctx.Services.AddSingleton(TimeProvider.System);
        ctx.Services.AddSingleton<IWorkOrderBuilder>(new StubWorkOrderBuilder(workOrderId, creator, title));
        ctx.Services.AddSingleton<IUserSession>(new StubUserSession(creator));
        ctx.Services.AddSingleton<ITranslationService>(new StubTranslationService());
        ctx.Services.AddSpeechSynthesis();
        ctx.Services.AddSpeechRecognition();

        var navigationManager = ctx.Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo(navigationManager.GetUriWithQueryParameter("Mode", "New"));

        return ctx;
    }

    private class StubWorkOrderManageBus(WorkOrder? workOrderByNumber = null) : Bus(null!)
    {
        public override Task Publish(INotification notification) => Task.CompletedTask;

        public override Task<TResponse> Send<TResponse>(IRequest<TResponse> request)
        {
            if (request is EmployeeGetAllQuery)
                return Task.FromResult((TResponse)(object)Array.Empty<Employee>());

            if (request is WorkOrderAttachmentsQuery)
                return Task.FromResult((TResponse)(object)Array.Empty<WorkOrderAttachment>());

            if (request is WorkOrderByNumberQuery && workOrderByNumber != null)
                return Task.FromResult((TResponse)(object)workOrderByNumber);

            throw new NotImplementedException($"Unhandled request type: {request.GetType().Name}");
        }
    }

    private class StubWorkOrderBuilder(Guid workOrderId, Employee creator, string? title) : IWorkOrderBuilder
    {
        public WorkOrder CreateNewWorkOrder(Employee currentUser)
        {
            return new WorkOrder
            {
                Id = workOrderId,
                Number = "WO-TITLE",
                Status = WorkOrderStatus.Draft,
                Creator = creator,
                Title = title,
                Description = "Char count test"
            };
        }
    }

    private class StubUserSession(Employee user) : IUserSession
    {
        public Task<Employee?> GetCurrentUserAsync() => Task.FromResult<Employee?>(user);
    }

    private class StubTranslationService : ITranslationService
    {
        public Task<string> TranslateAsync(string text, string targetLanguageCode) => Task.FromResult(text);
    }
}
