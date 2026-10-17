using System.Net;
using ReworkGuard.Application.Bugs;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Deductions;
using ReworkGuard.Application.Payroll;
using ReworkGuard.Application.TimeLogs;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Tests.Support;

/// <summary>
/// The standing cast for Phase B integration tests: Priya (Monthly 48,000 = 300/h) and Hana (Hourly 250) delivered a task
/// with 6 h and 2 h of approved regular work (shares 75% / 25%), plus a lead (their manager), QA, a manager and finance.
/// </summary>
public sealed class ReworkScenario
{
    private ReworkScenario(ApiFactory api, TestClock clock)
    {
        Api = api;
        Clock = clock;
    }

    public ApiFactory Api { get; }

    public TestClock Clock { get; }

    public int TaskId { get; private set; }

    public Employee Priya { get; private set; } = null!;

    public Employee Hana { get; private set; } = null!;

    public Employee LeadEmployee { get; private set; } = null!;

    public Employee ManagerEmployee { get; private set; } = null!;

    public Employee FinanceEmployee { get; private set; } = null!;

    public HttpClient PriyaClient { get; private set; } = null!;

    public HttpClient HanaClient { get; private set; } = null!;

    public HttpClient Lead { get; private set; } = null!;

    public HttpClient Qa { get; private set; } = null!;

    public HttpClient Manager { get; private set; } = null!;

    public HttpClient Finance { get; private set; } = null!;

    public static async Task<ReworkScenario> CreateAsync(TestData data, ApiFactory api, TestClock clock, int warrantyDays = 45)
    {
        var s = new ReworkScenario(api, clock);
        s.ManagerEmployee = await data.EmployeeAsync(Role.Manager);
        s.LeadEmployee = await data.EmployeeAsync(Role.TeamLead, managerId: s.ManagerEmployee.Id);
        s.Priya = await data.EmployeeAsync(Role.Employee, PayMode.Monthly, 48000m, managerId: s.LeadEmployee.Id);
        s.Hana = await data.EmployeeAsync(Role.Employee, PayMode.Hourly, 250m, managerId: s.LeadEmployee.Id);
        var qa = await data.EmployeeAsync(Role.QA);
        s.FinanceEmployee = await data.EmployeeAsync(Role.Finance);
        var task = await data.DeliveredTaskAsync(warrantyDays, clock.UtcNow.AddDays(-9), s.LeadEmployee, (s.Priya, 6m), (s.Hana, 2m));
        s.TaskId = task.Id;

        s.PriyaClient = await api.ClientForAsync(s.Priya);
        s.HanaClient = await api.ClientForAsync(s.Hana);
        s.Lead = await api.ClientForAsync(s.LeadEmployee);
        s.Qa = await api.ClientForAsync(qa);
        s.Manager = await api.ClientForAsync(s.ManagerEmployee);
        s.Finance = await api.ClientForAsync(s.FinanceEmployee);
        return s;
    }

    public async Task<int> ReportBugAsync(DateOnly? foundDate = null, int? taskId = null) =>
        (await (await Qa.PostJsonAsync($"/api/v1/tasks/{taskId ?? TaskId}/bugs", new ReportBugRequest(BugSeverity.High, "Declined cards are charged twice", foundDate ?? Clock.Today)))
            .ReadAsync<BugReportedDto>(HttpStatusCode.Created)).Bug.Id;

    public async Task<int> LogCorrectiveAsync(HttpClient who, int bugId, decimal hours, DateOnly? workDate = null, int? taskId = null) =>
        (await (await who.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(taskId ?? TaskId, workDate ?? Clock.Today, hours, WorkType.Corrective, bugId, null)))
            .ReadAsync<TimeLogDto>(HttpStatusCode.Created)).Id;

    public async Task ApproveLogAsync(int timeLogId) =>
        await (await Lead.PostAsync(new Uri($"/api/v1/timelogs/{timeLogId}/approve", UriKind.Relative), null)).ShouldHaveStatusAsync(HttpStatusCode.OK);

    public async Task ReviewAsync(int bugId, RootCause cause = RootCause.OwnDefect, bool caused = true, string? justification = null) =>
        await (await Manager.PostJsonAsync($"/api/v1/bugs/{bugId}/review", new ReviewBugRequest(cause, caused, justification))).ShouldHaveStatusAsync(HttpStatusCode.OK);

    /// <summary>Bug, Priya logs <paramref name="hours"/> corrective hours today, lead approves, manager confirms OwnDefect.</summary>
    public async Task<(int BugId, int TimeLogId)> DeductibleBugAsync(decimal hours = 4m)
    {
        var bugId = await ReportBugAsync();
        var logId = await LogCorrectiveAsync(PriyaClient, bugId, hours);
        await ApproveLogAsync(logId);
        await ReviewAsync(bugId);
        return (bugId, logId);
    }

    public static async Task<IReadOnlyList<DeductionDto>> LinesAsync(HttpClient who, string query = "")
    {
        var page = await (await who.GetAsync(new Uri($"/api/v1/deductions?pageSize=100{query}", UriKind.Relative))).ReadAsync<PagedResult<DeductionDto>>();
        return page.Items;
    }

    public async Task<IReadOnlyList<DeductionDto>> NotifyAsync(params int[] ids) =>
        await (await Manager.PostJsonAsync("/api/v1/deductions/notify", new DeductionIdsRequest(ids))).ReadAsync<IReadOnlyList<DeductionDto>>();

    public async Task<IReadOnlyList<DeductionDto>> ApproveAsync(params int[] ids) =>
        await (await Manager.PostJsonAsync("/api/v1/deductions/approve", new DeductionIdsRequest(ids))).ReadAsync<IReadOnlyList<DeductionDto>>();

    /// <summary>Notifies the lines, moves the clock past the dispute window (if it is earlier) and approves them.</summary>
    public async Task<IReadOnlyList<DeductionDto>> NotifyAndApproveAsync(params int[] ids)
    {
        var notified = await NotifyAsync(ids);
        var windowEnd = notified.Max(l => l.DisputeWindowEndsAtUtc!.Value);
        if (Clock.UtcNow <= windowEnd)
        {
            Clock.UtcNow = windowEnd.AddTicks(1);
        }

        return await ApproveAsync(ids);
    }

    /// <summary>All Draft lines of a bug, notified and approved.</summary>
    public async Task<IReadOnlyList<DeductionDto>> ApproveBugLinesAsync(int bugId)
    {
        var drafts = await LinesAsync(Manager, $"&bugId={bugId}&status=Draft");
        return await NotifyAndApproveAsync(drafts.Select(l => l.Id).ToArray());
    }

    public async Task<PayrollRunDetailDto> GenerateRunAsync(string month) =>
        await (await Finance.PostJsonAsync("/api/v1/payroll/runs", new GeneratePayrollRunRequest(month))).ReadAsync<PayrollRunDetailDto>();

    public static async Task<PayrollRunDetailDto> StepAsync(HttpClient who, PayrollRunDetailDto run, string step) =>
        await (await who.PostJsonAsync($"/api/v1/payroll/runs/{run.Run.Id}/{step}", new PayrollRunStepRequest(run.Run.Version))).ReadAsync<PayrollRunDetailDto>();

    /// <summary>Generate, review (Finance), approve (Manager), finalize and lock a month.</summary>
    public async Task<PayrollRunDetailDto> RunPayrollAsync(string month)
    {
        var run = await GenerateRunAsync(month);
        run = await StepAsync(Finance, run, "review");
        run = await StepAsync(Manager, run, "approve");
        run = await StepAsync(Finance, run, "finalize");
        return await StepAsync(Finance, run, "lock");
    }

    public static async Task<DeductionPreviewDto> PreviewAsync(HttpClient who, int employeeId, string? month = null) =>
        await (await who.GetAsync(new Uri($"/api/v1/deductions/preview?employeeId={employeeId}" + (month is null ? string.Empty : $"&periodMonth={month}"), UriKind.Relative)))
            .ReadAsync<DeductionPreviewDto>();
}
