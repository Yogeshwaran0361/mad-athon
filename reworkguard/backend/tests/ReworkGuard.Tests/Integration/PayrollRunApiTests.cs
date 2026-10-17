using System.Net;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using ReworkGuard.Application.Attribution;
using ReworkGuard.Application.Payroll;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

/// <summary>
/// The payroll run workflow through the API against MySQL: generate, review, approve, finalize, lock; versioning,
/// sequencing, carry-forward, refunds, manual settlement, salary bases, concurrency and the database guards.
/// Clock: Tuesday 10 March 2026; lines approved after their dispute window are due in April 2026.
/// </summary>
public sealed class PayrollRunApiTests : ApiTestBase
{
    public PayrollRunApiTests(MySqlContainerFixture mysql)
        : base(mysql)
    {
    }

    [Fact]
    public async Task Generate_review_approve_finalize_lock_moves_lines_and_locks_the_month()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var (bugId, _) = await s.DeductibleBugAsync(hours: 4m);
        var lines = await s.ApproveBugLinesAsync(bugId);

        var run = await s.GenerateRunAsync("2026-04");
        run.Run.Should().BeEquivalentTo(new { PeriodMonth = "2026-04", RunVersion = 1, Status = PayrollRunStatus.Preview, CapPercent = 20m });
        run.Run.InputsHash.Should().MatchRegex("^[0-9a-f]{64}$");
        var activeEmployees = await FreshDb().Employees.CountAsync(e => e.IsActive);
        run.Run.EmployeeCount.Should().Be(activeEmployees, "every active employee gets a row, with or without deductions");
        run.Employees.Single(e => e.EmployeeId == s.Priya.Id).Should().BeEquivalentTo(new
        {
            SalaryBase = 48000.00m, CapAmount = 9600.00m, CurrentDue = 900.00m, Applied = 900.00m, CarriedForward = 0m, NetAdjustment = -900.00m, ManualSettlement = false,
        });
        run.Employees.Single(e => e.EmployeeId == s.Hana.Id).Should().BeEquivalentTo(new { CurrentDue = 250.00m, Applied = 0m, CarriedForward = 250.00m });
        run.Run.TotalApplied.Should().Be(900.00m);
        run.Run.TotalCarriedForward.Should().Be(250.00m);
        run.Reconciliation.Should().BeNull();

        run = await ReworkScenario.StepAsync(s.Finance, run, "review");
        run.Run.Status.Should().Be(PayrollRunStatus.Reviewed);
        run = await ReworkScenario.StepAsync(s.Manager, run, "approve");
        run.Run.Status.Should().Be(PayrollRunStatus.Approved);
        run.Run.ApprovedByEmployeeId.Should().Be(s.ManagerEmployee.Id);

        run = await ReworkScenario.StepAsync(s.Finance, run, "finalize");
        run.Run.Status.Should().Be(PayrollRunStatus.Finalized);
        run.Reconciliation.Should().HaveCount(6).And.OnlyContain(c => c.Passed);
        (await StatusesAsync(lines)).Should().OnlyContain(x => x == DeductionStatus.Locked);
        var periods = await (await s.Finance.GetAsync(new Uri("/api/v1/payroll/periods", UriKind.Relative))).ReadAsync<List<PayrollPeriodDto>>();
        periods.Should().ContainSingle(p => p.Month == "2026-04").Which.Should().Match<PayrollPeriodDto>(p => p.IsLocked && p.FinalizedRunId == run.Run.Id);

        run = await ReworkScenario.StepAsync(s.Finance, run, "lock");
        run.Run.Status.Should().Be(PayrollRunStatus.Locked);
        (await StatusesAsync(lines)).Should().OnlyContain(x => x == DeductionStatus.Applied);
        await using var db = FreshDb();
        (await db.Deductions.Where(d => lines.Select(l => l.Id).Contains(d.Id)).Select(d => d.PayrollRunId).Distinct().ToListAsync())
            .Should().Equal(run.Run.Id);
        (await db.AuditEvents.Where(a => a.EntityType == "PayrollRun" && a.EntityId == run.Run.Id.ToString()).OrderBy(a => a.Id).Select(a => a.Action).ToListAsync())
            .Should().Equal("Generated", "Reviewed", "Approved", "Finalized", "Locked");
        (await db.Notifications.Where(n => n.Type == NotificationType.PayrollRunAwaitingApproval).Select(n => n.RecipientEmployeeId).ToListAsync())
            .Should().Equal(s.ManagerEmployee.Id);
        (await db.Notifications.Where(n => n.Type == NotificationType.PayrollRunFinalized).Select(n => n.RecipientEmployeeId).ToListAsync())
            .Should().BeEquivalentTo([s.FinanceEmployee.Id, s.ManagerEmployee.Id]);

        var detail = await (await s.Manager.GetAsync(new Uri($"/api/v1/payroll/runs/{run.Run.Id}/employees/{s.Priya.Id}", UriKind.Relative)))
            .ReadAsync<PayrollRunEmployeeDetailDto>();
        detail.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Kind = PayrollItemKind.Current, Due = 900.00m, Applied = 900.00m, BugId = bugId, TaskId = s.TaskId });
    }

    [Fact]
    public async Task Each_step_requires_the_version_the_caller_saw()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var run = await s.GenerateRunAsync("2026-04");

        await (await s.Finance.PostJsonAsync($"/api/v1/payroll/runs/{run.Run.Id}/review", new PayrollRunStepRequest(run.Run.Version + 1)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "payroll.stale");
        await (await s.Finance.PostJsonAsync($"/api/v1/payroll/runs/{run.Run.Id}/review", new PayrollRunStepRequest(-1)))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        await (await s.Finance.PostJsonAsync("/api/v1/payroll/runs/999999/review", new PayrollRunStepRequest(1)))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound);

        var reviewed = await ReworkScenario.StepAsync(s.Finance, run, "review");
        await (await s.Manager.PostJsonAsync($"/api/v1/payroll/runs/{run.Run.Id}/approve", new PayrollRunStepRequest(run.Run.Version)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "payroll.stale");
        await (await s.Finance.PostJsonAsync($"/api/v1/payroll/runs/{run.Run.Id}/finalize", new PayrollRunStepRequest(reviewed.Run.Version)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "payroll.invalid_transition");
    }

    [Fact]
    public async Task Regenerating_supersedes_the_open_run_with_a_new_version()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var first = await ReworkScenario.StepAsync(s.Finance, await s.GenerateRunAsync("2026-04"), "review");

        var second = await s.GenerateRunAsync("2026-04");

        second.Run.RunVersion.Should().Be(2);
        var runs = await (await s.Finance.GetAsync(new Uri("/api/v1/payroll/runs?periodMonth=2026-04", UriKind.Relative))).ReadAsync<List<PayrollRunSummaryDto>>();
        runs.Select(r => (r.RunVersion, r.Status)).Should().Equal((2, PayrollRunStatus.Preview), (1, PayrollRunStatus.Superseded));
        await (await s.Manager.PostJsonAsync($"/api/v1/payroll/runs/{first.Run.Id}/approve", new PayrollRunStepRequest(runs[1].Version)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "payroll.invalid_transition");
        await (await s.Finance.GetAsync(new Uri("/api/v1/payroll/runs?periodMonth=April", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_line_approved_after_the_run_was_generated_or_approved_blocks_it()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var (firstBug, _) = await s.DeductibleBugAsync(hours: 2m);
        await s.ApproveBugLinesAsync(firstBug);
        var reviewed = await ReworkScenario.StepAsync(s.Finance, await s.GenerateRunAsync("2026-04"), "review");

        await s.ApproveLogAsync(await s.LogCorrectiveAsync(s.PriyaClient, firstBug, 1m));
        await s.ApproveBugLinesAsync(firstBug);
        await (await s.Manager.PostJsonAsync($"/api/v1/payroll/runs/{reviewed.Run.Id}/approve", new PayrollRunStepRequest(reviewed.Run.Version)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "payroll.inputs_changed");

        var approved = await ReworkScenario.StepAsync(s.Manager, await ReworkScenario.StepAsync(s.Finance, await s.GenerateRunAsync("2026-04"), "review"), "approve");
        await SetCapPercentAsync("10");
        await (await s.Finance.PostJsonAsync($"/api/v1/payroll/runs/{approved.Run.Id}/finalize", new PayrollRunStepRequest(approved.Run.Version)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "payroll.inputs_changed");
        (await FreshDb().PayrollPeriods.SingleAsync(p => p.Month == "2026-04")).IsLocked.Should().BeFalse();
    }

    [Fact]
    public async Task Months_run_in_order_and_a_finalized_month_cannot_be_rerun()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);

        await (await s.Finance.PostJsonAsync("/api/v1/payroll/runs", new GeneratePayrollRunRequest("2026-05")))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "payroll.too_early");
        await (await s.Finance.PostJsonAsync("/api/v1/payroll/runs", new GeneratePayrollRunRequest("2026-4")))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        var march = await s.GenerateRunAsync("2026-03");
        march = await ReworkScenario.StepAsync(s.Finance, march, "review");
        march = await ReworkScenario.StepAsync(s.Manager, march, "approve");
        march = await ReworkScenario.StepAsync(s.Finance, march, "finalize");

        await (await s.Finance.PostJsonAsync("/api/v1/payroll/runs", new GeneratePayrollRunRequest("2026-03")))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "payroll.month_locked");
        await (await s.Finance.PostJsonAsync("/api/v1/payroll/runs", new GeneratePayrollRunRequest("2026-02")))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "payroll.out_of_sequence");

        // April can be generated and approved, but not finalized until March is locked.
        var april = await ReworkScenario.StepAsync(s.Manager, await ReworkScenario.StepAsync(s.Finance, await s.GenerateRunAsync("2026-04"), "review"), "approve");
        await (await s.Finance.PostJsonAsync($"/api/v1/payroll/runs/{april.Run.Id}/finalize", new PayrollRunStepRequest(april.Run.Version)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "payroll.previous_not_locked");
        await ReworkScenario.StepAsync(s.Finance, march, "lock");
        (await ReworkScenario.StepAsync(s.Finance, april, "finalize")).Run.Status.Should().Be(PayrollRunStatus.Finalized);
    }

    [Fact]
    public async Task Work_for_a_finalized_month_is_routed_to_the_next_open_month()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        await s.RunPayrollAsync("2026-04");

        var (bugId, _) = await s.DeductibleBugAsync(hours: 2m);

        var line = (await ReworkScenario.LinesAsync(s.Manager, $"&bugId={bugId}&employeeId={s.Priya.Id}")).Single();
        line.SourceMonth.Should().Be("2026-03");
        line.ApplyMonth.Should().Be("2026-05");
        (await FreshDb().Deductions.SingleAsync(d => d.Id == line.Id)).RoutedFromLockedMonth.Should().BeTrue();
    }

    [Fact]
    public async Task An_override_after_payment_refunds_in_the_next_run_and_cancels_outstanding_carry_first()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var (bugId, _) = await s.DeductibleBugAsync(hours: 4m);
        var original = (await s.ApproveBugLinesAsync(bugId)).Single(l => l.EmployeeId == s.Priya.Id);
        await SetCapPercentAsync("1"); // 480 of Priya's 900 is applied in April, 420 carries forward
        await s.RunPayrollAsync("2026-04");

        // The manager halves Priya's share: her paid line (900) is compensated in full and a new 600 line is drafted.
        await (await s.Manager.PutJsonAsync($"/api/v1/bugs/{bugId}/attribution", new OverrideAttributionRequest(
            [new ShareInput(s.Priya.Id, 50m), new ShareInput(s.Hana.Id, 50m)], "Pairing on the gateway code was even"))).ShouldHaveStatusAsync(HttpStatusCode.OK);
        var priyaLines = await ReworkScenario.LinesAsync(s.Manager, $"&employeeId={s.Priya.Id}");
        var compensation = priyaLines.Single(l => l.Kind == DeductionKind.Compensating);
        compensation.Should().BeEquivalentTo(new { Amount = -900.00m, Status = DeductionStatus.Approved, ApplyMonth = "2026-05", ReversesDeductionId = (int?)original.Id });
        priyaLines.Should().ContainSingle(l => l.Status == DeductionStatus.Draft && l.Amount == 600.00m);

        Clock.UtcNow = new DateTime(2026, 4, 10, 9, 0, 0, DateTimeKind.Utc);
        var may = await s.RunPayrollAsync("2026-05");

        // Of the 900 refund, 420 cancels the carry still owed and 480 is paid back.
        var priya = may.Employees.Single(e => e.EmployeeId == s.Priya.Id);
        priya.Should().BeEquivalentTo(new { CarriedIn = 420.00m, CancelledByCompensation = 420.00m, Refunds = -480.00m, Applied = 0m, CarriedForward = 0m, NetAdjustment = 480.00m });
        may.Run.TotalRefunds.Should().Be(-480.00m);
        var statuses = await FreshDb().Deductions.Where(d => d.Id == original.Id || d.Id == compensation.Id).ToDictionaryAsync(d => d.Id, d => d.Status);
        statuses[original.Id].Should().Be(DeductionStatus.Reversed);
        statuses[compensation.Id].Should().Be(DeductionStatus.Applied);
    }

    [Fact]
    public async Task An_override_while_the_line_is_locked_is_compensated_when_the_run_locks()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var (bugId, _) = await s.DeductibleBugAsync(hours: 4m);
        await s.ApproveBugLinesAsync(bugId);
        var run = await s.GenerateRunAsync("2026-04");
        run = await ReworkScenario.StepAsync(s.Manager, await ReworkScenario.StepAsync(s.Finance, run, "review"), "approve");
        run = await ReworkScenario.StepAsync(s.Finance, run, "finalize");

        await (await s.Manager.PutJsonAsync($"/api/v1/bugs/{bugId}/attribution", new OverrideAttributionRequest(
            [new ShareInput(s.Priya.Id, 50m), new ShareInput(s.Hana.Id, 50m)], "Pairing on the gateway code was even"))).ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await ReworkScenario.LinesAsync(s.Manager, $"&employeeId={s.Priya.Id}")).Should().ContainSingle("the locked line waits for its run");

        await ReworkScenario.StepAsync(s.Finance, run, "lock");

        var lines = await ReworkScenario.LinesAsync(s.Manager, $"&employeeId={s.Priya.Id}");
        lines.Select(l => (l.Kind, l.Status, l.Amount, l.ApplyMonth)).Should().BeEquivalentTo(new[]
        {
            (DeductionKind.Standard, DeductionStatus.Applied, 900.00m, "2026-04"),
            (DeductionKind.Compensating, DeductionStatus.Approved, -900.00m, "2026-05"),
            (DeductionKind.Standard, DeductionStatus.Draft, 600.00m, "2026-05"),
        });
    }

    [Fact]
    public async Task Someone_who_has_left_is_settled_manually_not_capped()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var (bugId, _) = await s.DeductibleBugAsync(hours: 4m);
        await s.ApproveBugLinesAsync(bugId);
        await Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Employees SET IsActive = 0 WHERE Id = {s.Priya.Id}");
        var other = await Data.EmployeeAsync(Role.Employee);
        await Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Employees SET IsActive = 0 WHERE Id = {other.Id}");

        var run = await s.RunPayrollAsync("2026-04");

        run.Employees.Single(e => e.EmployeeId == s.Priya.Id).Should().BeEquivalentTo(new
        {
            ManualSettlement = true, CapAmount = 0m, CurrentDue = 900.00m, Applied = 0m, CarriedForward = 0m, SettledManually = 900.00m,
        });
        run.Employees.Should().NotContain(e => e.EmployeeId == other.Id, "a leaver with nothing owed is left out");
        run.Run.TotalSettledManually.Should().Be(900.00m);
    }

    [Fact]
    public async Task Salary_bases_follow_pay_mode_and_rate_history()
    {
        var finance = await Data.EmployeeAsync(Role.Finance);
        var approver = await Data.EmployeeAsync(Role.TeamLead);
        var monthly = await Data.EmployeeAsync(Role.Employee, PayMode.Monthly, 48000m);
        var hourly = await Data.EmployeeAsync(Role.Employee, PayMode.Hourly, 250m);
        var daily = await Data.EmployeeAsync(Role.Employee, PayMode.Daily, 2000m);
        var recordedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        monthly.ChangePay(PayMode.Monthly, 60000m, null, null, new DateOnly(2026, 4, 15), new DateOnly(2026, 3, 1), finance.Id, recordedAt);
        hourly.ChangePay(PayMode.Hourly, null, null, 300m, new DateOnly(2026, 4, 3), new DateOnly(2026, 3, 1), finance.Id, recordedAt);
        var task = await Data.TaskAsync(30, hourly, daily);
        var at = new DateTime(2026, 4, 30, 9, 0, 0, DateTimeKind.Utc);
        task.MoveTo(WorkTaskStatus.InProgress, hourly.Id, "start", at);
        foreach (var (who, day, hours) in new[] { (hourly, 2, 10m), (hourly, 3, 5m), (daily, 2, 8m), (daily, 3, 4m), (daily, 6, 10m), (daily, 7, 3m) })
        {
            var log = task.LogRegularHours(who.Id, new DateOnly(2026, 4, day), hours, null, 0m, at);
            if (day != 7)
            {
                log.Approve(approver.Id, at); // the 3 h on the 7th stay unapproved and do not count
            }

            Db.TimeLogs.Add(log);
        }

        await Db.SaveChangesAsync();

        var run = await (await (await Api.ClientForAsync(finance)).PostJsonAsync("/api/v1/payroll/runs", new GeneratePayrollRunRequest("2026-04"))).ReadAsync<PayrollRunDetailDto>();

        // Monthly: gross in force on 1 April. Hourly: 10 h × 250 + 5 h × 300. Daily: (1 + 0.5 + 1) days × 2000.
        run.Employees.Single(e => e.EmployeeId == monthly.Id).SalaryBase.Should().Be(48000.00m);
        run.Employees.Single(e => e.EmployeeId == hourly.Id).SalaryBase.Should().Be(4000.00m);
        var dailyRow = run.Employees.Single(e => e.EmployeeId == daily.Id);
        dailyRow.SalaryBase.Should().Be(5000.00m);
        dailyRow.SalaryBasis.Should().Be("22 approved hours (2.5 days) in 2026-04");
        dailyRow.CapAmount.Should().Be(1000.00m);

        var may = await (await (await Api.ClientForAsync(finance)).GetAsync(new Uri($"/api/v1/deductions/preview?employeeId={monthly.Id}&periodMonth=2026-05", UriKind.Relative)))
            .ReadAsync<DeductionPreviewDto>();
        may.SalaryBase.Should().Be(60000.00m);
    }

    [Fact]
    public async Task Concurrent_generates_serialise_and_a_run_is_finalized_once()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var (bugId, _) = await s.DeductibleBugAsync(hours: 4m);
        await s.ApproveBugLinesAsync(bugId);
        var otherFinance = await Api.ClientForAsync(await Data.EmployeeAsync(Role.Finance));

        var generated = await Task.WhenAll(
            s.Finance.PostJsonAsync("/api/v1/payroll/runs", new GeneratePayrollRunRequest("2026-04")),
            otherFinance.PostJsonAsync("/api/v1/payroll/runs", new GeneratePayrollRunRequest("2026-04")));
        generated.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        var runs = await (await s.Finance.GetAsync(new Uri("/api/v1/payroll/runs?periodMonth=2026-04", UriKind.Relative))).ReadAsync<List<PayrollRunSummaryDto>>();
        runs.Select(r => r.RunVersion).Should().BeEquivalentTo([1, 2]);
        runs.Count(r => r.Status == PayrollRunStatus.Preview).Should().Be(1);

        var run = await (await s.Finance.GetAsync(new Uri($"/api/v1/payroll/runs/{runs[0].Id}", UriKind.Relative))).ReadAsync<PayrollRunDetailDto>();
        run = await ReworkScenario.StepAsync(s.Manager, await ReworkScenario.StepAsync(s.Finance, run, "review"), "approve");
        var finalized = await Task.WhenAll(
            s.Finance.PostJsonAsync($"/api/v1/payroll/runs/{run.Run.Id}/finalize", new PayrollRunStepRequest(run.Run.Version)),
            otherFinance.PostJsonAsync($"/api/v1/payroll/runs/{run.Run.Id}/finalize", new PayrollRunStepRequest(run.Run.Version)));

        finalized.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        await finalized.Single(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBeProblemAsync(HttpStatusCode.Conflict, "payroll.stale");
        (await FreshDb().AuditEvents.CountAsync(a => a.EntityType == "PayrollRun" && a.Action == "Finalized")).Should().Be(1);
    }

    [Fact]
    public async Task Only_finance_runs_payroll_and_only_a_manager_approves()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var admin = await Api.ClientForAsync(await Data.EmployeeAsync(Role.Admin));

        await (await s.Manager.PostJsonAsync("/api/v1/payroll/runs", new GeneratePayrollRunRequest("2026-04"))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await admin.PostJsonAsync("/api/v1/payroll/runs", new GeneratePayrollRunRequest("2026-04"))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        var run = await ReworkScenario.StepAsync(s.Finance, await s.GenerateRunAsync("2026-04"), "review");
        await (await s.Finance.PostJsonAsync($"/api/v1/payroll/runs/{run.Run.Id}/approve", new PayrollRunStepRequest(run.Run.Version))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await s.PriyaClient.GetAsync(new Uri("/api/v1/payroll/runs", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await s.Lead.GetAsync(new Uri($"/api/v1/payroll/runs/{run.Run.Id}", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await admin.GetAsync(new Uri($"/api/v1/payroll/runs/{run.Run.Id}", UriKind.Relative))).ShouldHaveStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_database_keeps_run_figures_and_locked_months_immutable()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var (bugId, _) = await s.DeductibleBugAsync(hours: 4m);
        await s.ApproveBugLinesAsync(bugId);
        var run = await s.RunPayrollAsync("2026-04");
        var id = run.Run.Id;

        var attempts = new Dictionary<string, string>
        {
            ["figures"] = $"UPDATE PayrollRuns SET TotalApplied = 1 WHERE Id = {id}",
            ["status back"] = $"UPDATE PayrollRuns SET Status = 'Approved' WHERE Id = {id}",
            ["delete run"] = $"DELETE FROM PayrollRuns WHERE Id = {id}",
            ["update employee row"] = $"UPDATE PayrollRunEmployees SET Applied = 0 WHERE PayrollRunId = {id}",
            ["delete employee row"] = $"DELETE FROM PayrollRunEmployees WHERE PayrollRunId = {id}",
            ["update item"] = "UPDATE PayrollRunItems SET Applied = 0",
            ["delete item"] = "DELETE FROM PayrollRunItems",
            ["reopen month"] = "UPDATE PayrollPeriods SET IsLocked = 0 WHERE Month = '2026-04'",
        };

        foreach (var (name, sql) in attempts)
        {
            var act = () => Db.Database.ExecuteSqlRawAsync(sql);
            (await act.Should().ThrowAsync<MySqlException>(name)).Which.SqlState.Should().Be("45000");
        }
    }

    private async Task<List<DeductionStatus>> StatusesAsync(IEnumerable<Application.Deductions.DeductionDto> lines)
    {
        var ids = lines.Select(l => l.Id).ToList();
        return await FreshDb().Deductions.Where(d => ids.Contains(d.Id)).Select(d => d.Status).ToListAsync();
    }

    private async Task SetCapPercentAsync(string value)
    {
        await Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE PolicySettings SET Value = {value} WHERE `Key` = {PolicySetting.DeductionCapPercent}");
    }
}
