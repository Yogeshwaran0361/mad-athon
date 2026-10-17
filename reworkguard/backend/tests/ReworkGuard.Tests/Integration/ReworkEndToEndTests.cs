using System.Net;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Attribution;
using ReworkGuard.Application.Bugs;
using ReworkGuard.Application.Payroll;
using ReworkGuard.Application.TimeLogs;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

/// <summary>
/// The whole Pay-Once Ledger flow through the HTTP API against MySQL: bug report, auto-reopen, corrective logging, lead
/// approval, manager review, deduction lines notified and approved, the pay-month preview and the payroll run.
/// </summary>
public sealed class ReworkEndToEndTests : ApiTestBase
{
    public ReworkEndToEndTests(MySqlContainerFixture mysql)
        : base(mysql)
    {
    }

    [Fact]
    public async Task Bug_reopen_log_approve_review_approve_lines_preview()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);

        // QA reports a bug inside the warranty window: the task reopens and both deliverers are named.
        var reported = await (await s.Qa.PostJsonAsync($"/api/v1/tasks/{s.TaskId}/bugs", new ReportBugRequest(BugSeverity.High, "Declined cards are charged twice", Clock.Today.AddDays(-1))))
            .ReadAsync<BugReportedDto>(HttpStatusCode.Created);
        reported.Bug.TaskStatus.Should().Be(WorkTaskStatus.Reopened);
        reported.Bug.WithinWarranty.Should().BeTrue();
        reported.OriginalDeliverers.Select(d => d.EmployeeId).Should().BeEquivalentTo([s.Priya.Id, s.Hana.Id]);
        var bugId = reported.Bug.Id;

        // Regular hours are refused now; corrective hours are accepted and start the fix.
        await (await s.PriyaClient.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(s.TaskId, Clock.Today, 1m, WorkType.Regular, null, null)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "timelog.regular_not_allowed");
        var priyaLog = await (await s.PriyaClient.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(s.TaskId, Clock.Today, 4m, WorkType.Corrective, bugId, "Idempotency key")))
            .ReadAsync<TimeLogDto>(HttpStatusCode.Created);
        var hanaLog = await (await s.HanaClient.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(s.TaskId, Clock.Today.AddDays(-1), 2m, WorkType.Corrective, bugId, "Regression test")))
            .ReadAsync<TimeLogDto>(HttpStatusCode.Created);
        priyaLog.TaskStatus.Should().Be(WorkTaskStatus.UnderFix);

        // Before approval and review, nothing is deductible or even pending.
        var before = await ReworkScenario.PreviewAsync(s.Manager, s.Priya.Id, "2026-04");
        before.CurrentDue.Should().Be(0m);
        before.Pending.Should().BeEmpty();

        // The team lead approves both corrective logs; still nothing until the manager reviews.
        await s.ApproveLogAsync(priyaLog.Id);
        await s.ApproveLogAsync(hanaLog.Id);
        (await ReworkScenario.PreviewAsync(s.Manager, s.Priya.Id, "2026-04")).Pending.Should().BeEmpty();

        // The manager confirms it was the deliverers' own defect: Draft lines appear as pending, not yet in the figures.
        (await (await s.Manager.PostJsonAsync($"/api/v1/bugs/{bugId}/review", new ReviewBugRequest(RootCause.OwnDefect, true, null))).ReadAsync<BugDto>())
            .IsDeductible.Should().BeTrue();
        var attribution = await (await s.Manager.GetAsync(new Uri($"/api/v1/bugs/{bugId}/attribution", UriKind.Relative))).ReadAsync<AttributionDto>();
        attribution.Shares.Single(x => x.EmployeeId == s.Priya.Id).SharePercent.Should().Be(75.00m);
        var drafted = await ReworkScenario.PreviewAsync(s.PriyaClient, s.Priya.Id);
        drafted.PeriodMonth.Should().Be("2026-04", "March work is deducted in April when no month is given");
        drafted.CurrentDue.Should().Be(0m);
        drafted.Pending.Select(l => (l.Status, l.Due)).Should().Equal((DeductionStatus.Draft, 450.00m), (DeductionStatus.Draft, 900.00m));

        await s.ApproveBugLinesAsync(bugId);

        // Priya: Monthly 48000 -> 300/h, share 75%. Lines: 2h x 300 x 75% = 450 (day before), 4h x 300 x 75% = 900.
        var priya = await ReworkScenario.PreviewAsync(s.PriyaClient, s.Priya.Id, "2026-04");
        priya.PayrollRunId.Should().BeNull();
        priya.SalaryBase.Should().Be(48000.00m);
        priya.Lines.Select(l => (l.Hours, l.HourlyRate, l.SharePercent, l.Due, l.Applied)).Should().Equal(
            (2m, 300.00m, 75.00m, 450.00m, 450.00m),
            (4m, 300.00m, 75.00m, 900.00m, 900.00m));
        priya.Lines.Should().OnlyContain(l =>
            l.BugId == bugId && l.TaskId == s.TaskId && l.ModuleName == "Module A" && l.Kind == PayrollItemKind.Current && l.Status == DeductionStatus.Approved);
        priya.Pending.Should().BeEmpty();
        priya.CurrentDue.Should().Be(1350.00m);
        priya.CapAmount.Should().Be(9600.00m);
        priya.Applied.Should().Be(1350.00m);
        priya.CarriedForward.Should().Be(0m);
        priya.NetAdjustment.Should().Be(-1350.00m);

        // Hana: Hourly 250, share 25%: 2h x 250 x 25% = 125 and 4h x 250 x 25% = 250. Her April salary base is her
        // approved April hours, of which she has none yet, so the cap is 0 and the whole 375 would carry forward.
        var hana = await ReworkScenario.PreviewAsync(s.Finance, s.Hana.Id, "2026-04");
        hana.HourlyRate().Should().Be(250.00m);
        hana.Lines.Select(l => l.Due).Should().Equal(125.00m, 250.00m);
        hana.CurrentDue.Should().Be(375.00m);
        hana.SalaryBase.Should().Be(0m);
        hana.Applied.Should().Be(0m);
        hana.CarriedForward.Should().Be(375.00m);
    }

    [Fact]
    public async Task Cap_and_carry_forward_chain_through_finalized_runs()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var (bugId, _) = await s.DeductibleBugAsync(hours: 4m);
        await s.ApproveBugLinesAsync(bugId);
        await SetCapPercentAsync("1"); // Priya's cap: 1% of 48000 = 480

        var aprilEstimate = await ReworkScenario.PreviewAsync(s.Manager, s.Priya.Id, "2026-04");
        aprilEstimate.CurrentDue.Should().Be(900.00m);
        aprilEstimate.Applied.Should().Be(480.00m);
        aprilEstimate.CarriedForward.Should().Be(420.00m);

        var april = await s.RunPayrollAsync("2026-04");
        april.Run.Status.Should().Be(PayrollRunStatus.Locked);
        var aprilFinal = await ReworkScenario.PreviewAsync(s.Manager, s.Priya.Id, "2026-04");
        aprilFinal.PayrollRunId.Should().Be(april.Run.Id);
        aprilFinal.Source.Should().Be("Finalized payroll run, version 1");
        (aprilFinal.Applied, aprilFinal.CarriedForward).Should().Be((480.00m, 420.00m));

        Clock.UtcNow = new DateTime(2026, 4, 10, 9, 0, 0, DateTimeKind.Utc);
        var may = await ReworkScenario.PreviewAsync(s.Manager, s.Priya.Id, "2026-05");
        may.CarriedIn.Should().Be(420.00m);
        may.CurrentDue.Should().Be(0m);
        may.Applied.Should().Be(420.00m);
        may.CarriedForward.Should().Be(0m);
        may.Lines.Should().ContainSingle().Which.Should().Match<DeductionPreviewLineDto>(l =>
            l.Kind == PayrollItemKind.CarriedIn && l.ApplyMonth == "2026-04" && l.BugId == bugId && l.Due == 420.00m);

        var mayRun = await s.RunPayrollAsync("2026-05");
        mayRun.Employees.Single(e => e.EmployeeId == s.Priya.Id).Applied.Should().Be(420.00m);
        var june = await ReworkScenario.PreviewAsync(s.Manager, s.Priya.Id, "2026-06");
        (june.CarriedIn, june.Applied).Should().Be((0m, 0m));
    }

    [Fact]
    public async Task A_bug_cleared_by_the_manager_never_deducts()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var bugId = await s.ReportBugAsync();
        await s.ApproveLogAsync(await s.LogCorrectiveAsync(s.PriyaClient, bugId, 4m));

        await s.ReviewAsync(bugId, RootCause.NewRequirement, false, "Client asked for 3-D Secure after sign-off");

        var preview = await ReworkScenario.PreviewAsync(s.Manager, s.Priya.Id, "2026-04");
        preview.CurrentDue.Should().Be(0m);
        preview.Pending.Should().BeEmpty();
    }

    [Fact]
    public async Task A_manager_override_changes_the_drafted_share()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var (bugId, _) = await s.DeductibleBugAsync(hours: 4m);

        await (await s.Manager.PutJsonAsync($"/api/v1/bugs/{bugId}/attribution", new OverrideAttributionRequest(
            [new ShareInput(s.Priya.Id, 50m), new ShareInput(s.Hana.Id, 50m)], "Pairing on the gateway code was even"))).ShouldHaveStatusAsync(HttpStatusCode.OK);

        var pending = (await ReworkScenario.PreviewAsync(s.Manager, s.Priya.Id, "2026-04")).Pending.Should().ContainSingle().Subject;
        pending.SharePercent.Should().Be(50.00m);
        pending.Due.Should().Be(600.00m);
    }

    [Fact]
    public async Task Preview_is_scoped_to_self_team_and_payroll_roles()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var outsider = await Api.ClientForAsync(await Data.EmployeeAsync(Role.TeamLead));

        async Task<HttpResponseMessage> Get(HttpClient who, int employeeId) =>
            await who.GetAsync(new Uri($"/api/v1/deductions/preview?employeeId={employeeId}&periodMonth=2026-04", UriKind.Relative));

        await (await Get(s.PriyaClient, s.Hana.Id)).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await Get(s.Qa, s.Hana.Id)).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await Get(outsider, s.Hana.Id)).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await Get(s.Lead, s.Hana.Id)).ShouldHaveStatusAsync(HttpStatusCode.OK);
        await (await Get(s.PriyaClient, s.Priya.Id)).ShouldHaveStatusAsync(HttpStatusCode.OK);
        await (await Get(s.Finance, s.Priya.Id)).ShouldHaveStatusAsync(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("March")]
    [InlineData("2026/03")]
    public async Task Preview_rejects_malformed_months(string month)
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);

        var response = await s.Manager.GetAsync(new Uri($"/api/v1/deductions/preview?employeeId={s.Priya.Id}&periodMonth={month}", UriKind.Relative));

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().Contain("periodMonth");
    }

    private async Task SetCapPercentAsync(string value)
    {
        await Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE PolicySettings SET Value = {value} WHERE `Key` = {PolicySetting.DeductionCapPercent}");
    }
}

internal static class DeductionPreviewAssertions
{
    /// <summary>The rate used on the preview's lines (all lines of one employee share it when the rate did not change).</summary>
    public static decimal HourlyRate(this DeductionPreviewDto preview) => preview.Lines.Select(l => l.HourlyRate).Distinct().Single();
}
