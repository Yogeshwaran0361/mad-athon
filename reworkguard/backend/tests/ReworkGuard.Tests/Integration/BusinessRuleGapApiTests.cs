using System.Net;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Admin;
using ReworkGuard.Application.Attribution;
using ReworkGuard.Application.Bugs;
using ReworkGuard.Application.Tasks;
using ReworkGuard.Application.TimeLogs;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

/// <summary>QaMiss deductibility, regressions of a previous fix, and IST work dates.</summary>
public sealed class BusinessRuleGapApiTests : ApiTestBase
{
    public BusinessRuleGapApiTests(MySqlContainerFixture mysql)
        : base(mysql)
    {
    }

    [Fact]
    public async Task A_qa_miss_is_not_deductible_by_default()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var bugId = await s.ReportBugAsync();
        await s.ApproveLogAsync(await s.LogCorrectiveAsync(s.PriyaClient, bugId, 4m));

        await s.ReviewAsync(bugId, RootCause.QaMiss, caused: true, justification: "Test plan skipped the decline path");

        (await ReworkScenario.LinesAsync(s.Manager)).Should().BeEmpty();
    }

    [Fact]
    public async Task With_the_policy_on_a_qa_miss_deducts_the_configured_fraction_of_each_share()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var admin = await Api.ClientForAsync(await Data.EmployeeAsync(Role.Admin));
        await (await admin.PutJsonAsync("/api/v1/admin/policies", new PolicyDto(160m, 8m, 20m, 45, 3, QaMissDeductible: true, QaMissSharePercent: 50m, false)))
            .ShouldHaveStatusAsync(HttpStatusCode.OK);
        var bugId = await s.ReportBugAsync();
        await s.ApproveLogAsync(await s.LogCorrectiveAsync(s.PriyaClient, bugId, 4m));

        await s.ReviewAsync(bugId, RootCause.QaMiss, caused: true, justification: "Test plan skipped the decline path");

        var lines = await ReworkScenario.LinesAsync(s.Manager);
        lines.Select(l => (l.EmployeeId, l.SharePercent, l.Amount)).Should().BeEquivalentTo(
            [(s.Priya.Id, 37.50m, 450.00m), (s.Hana.Id, 12.50m, 125.00m)], "75% and 25% shares halved; 4 h × 300 × 37.5% and 4 h × 250 × 12.5%");
        var bug = await (await s.Manager.GetAsync(new Uri($"/api/v1/bugs/{bugId}", UriKind.Relative))).ReadAsync<BugDto>();
        bug.IsDeductible.Should().BeTrue();
    }

    [Fact]
    public async Task A_regression_of_a_previous_fix_is_attributed_to_whoever_delivered_the_fix()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);

        // First cycle: Hana fixes bug 1 alone, the fix is delivered and verified.
        var firstBug = await s.ReportBugAsync();
        await s.ApproveLogAsync(await s.LogCorrectiveAsync(s.HanaClient, firstBug, 3m));
        await (await s.HanaClient.PostJsonAsync($"/api/v1/tasks/{s.TaskId}/transitions", new TransitionTaskRequest(WorkTaskStatus.Completed, "Fix delivered")))
            .ShouldHaveStatusAsync(HttpStatusCode.OK);
        await (await s.Qa.PostAsync(new Uri($"/api/v1/bugs/{firstBug}/verify", UriKind.Relative), null)).ShouldHaveStatusAsync(HttpStatusCode.OK);

        // Second cycle: the fix broke something; Priya fixes it.
        var secondBug = await s.ReportBugAsync();
        await s.ApproveLogAsync(await s.LogCorrectiveAsync(s.PriyaClient, secondBug, 2m));
        await (await s.Manager.PostJsonAsync($"/api/v1/bugs/{secondBug}/review", new ReviewBugRequest(RootCause.OwnDefect, true, null, RegressionOfBugId: firstBug)))
            .ShouldHaveStatusAsync(HttpStatusCode.OK);

        var attribution = await (await s.Manager.GetAsync(new Uri($"/api/v1/bugs/{secondBug}/attribution", UriKind.Relative))).ReadAsync<AttributionDto>();
        attribution.Basis.Should().Be(AttributionBasis.FixHoursOnEarlierBug);
        attribution.RegressionOfBugId.Should().Be(firstBug);
        attribution.Shares.Should().ContainSingle().Which.Should().BeEquivalentTo(new { EmployeeId = s.Hana.Id, SharePercent = 100.00m, BasisHours = 3m });

        var lines = await ReworkScenario.LinesAsync(s.Manager, $"&bugId={secondBug}");
        lines.Should().ContainSingle().Which.Should().BeEquivalentTo(new { EmployeeId = s.Hana.Id, Amount = 500.00m }, "2 h × 250 × 100%");

        var task = await (await s.Manager.GetAsync(new Uri($"/api/v1/tasks/{s.TaskId}", UriKind.Relative))).ReadAsync<TaskDetailDto>();
        task.Task.ReopenCount.Should().Be(2, "each cycle keeps its own bug and lines");
    }

    [Fact]
    public async Task A_regression_must_point_to_an_earlier_fixed_bug_on_the_same_task()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var bugId = await s.ReportBugAsync();

        await (await s.Manager.PostJsonAsync($"/api/v1/bugs/{bugId}/review", new ReviewBugRequest(RootCause.OwnDefect, true, null, RegressionOfBugId: bugId)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "bug.regression_other_task");
        await (await s.Manager.PostJsonAsync($"/api/v1/bugs/{bugId}/review", new ReviewBugRequest(RootCause.OwnDefect, true, null, RegressionOfBugId: 999999)))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound);
        (await FreshDb().Bugs.SingleAsync(b => b.Id == bugId)).ReviewedAtUtc.Should().BeNull("a failed review changes nothing");
    }

    [Fact]
    public async Task Work_dates_follow_india_time_so_just_after_ist_midnight_is_already_tomorrow()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var bugId = await s.ReportBugAsync();

        // 19:00 UTC on 10 March is 00:30 IST on 11 March.
        Clock.UtcNow = new DateTime(2026, 3, 10, 19, 0, 0, DateTimeKind.Utc);
        var log = await (await s.PriyaClient.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(s.TaskId, new DateOnly(2026, 3, 11), 1m, WorkType.Corrective, bugId, null)))
            .ReadAsync<TimeLogDto>(HttpStatusCode.Created);
        log.WorkDate.Should().Be(new DateOnly(2026, 3, 11));

        await (await s.PriyaClient.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(s.TaskId, new DateOnly(2026, 3, 12), 1m, WorkType.Corrective, bugId, null)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "timelog.future_date");
    }

    [Fact]
    public async Task Corrective_work_either_side_of_ist_midnight_on_a_month_end_lands_in_different_months()
    {
        // 12:00 UTC on 28 Feb is 17:30 IST on 28 Feb.
        Clock.UtcNow = new DateTime(2026, 2, 28, 12, 0, 0, DateTimeKind.Utc);
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var bugId = await s.ReportBugAsync(new DateOnly(2026, 2, 28));
        var february = await s.LogCorrectiveAsync(s.PriyaClient, bugId, 2m, new DateOnly(2026, 2, 28));

        // 19:00 UTC on 28 Feb is already 00:30 IST on 1 March.
        Clock.UtcNow = new DateTime(2026, 2, 28, 19, 0, 0, DateTimeKind.Utc);
        var march = await s.LogCorrectiveAsync(s.PriyaClient, bugId, 1m, new DateOnly(2026, 3, 1));
        await s.ApproveLogAsync(february);
        await s.ApproveLogAsync(march);
        await s.ReviewAsync(bugId);

        var lines = await ReworkScenario.LinesAsync(s.PriyaClient);
        lines.Select(l => (l.TimeLogId, l.SourceMonth, l.ApplyMonth)).Should().BeEquivalentTo(
            [(february, "2026-02", "2026-03"), (march, "2026-03", "2026-04")]);
    }

    [Fact]
    public async Task Corrective_work_cannot_predate_the_bug()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var bugId = await s.ReportBugAsync();

        await (await s.PriyaClient.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(s.TaskId, Clock.Today.AddDays(-1), 1m, WorkType.Corrective, bugId, null)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "timelog.before_bug_found");
    }
}
