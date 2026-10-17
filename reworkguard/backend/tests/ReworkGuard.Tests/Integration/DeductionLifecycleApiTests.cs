using System.Net;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using ReworkGuard.Application.Attribution;
using ReworkGuard.Application.Bugs;
using ReworkGuard.Application.Deductions;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

/// <summary>Deduction lines from drafting to approval, both dispute branches, scoping and database guards.</summary>
public sealed class DeductionLifecycleApiTests : ApiTestBase
{
    // Clock: Tuesday 10 March 2026, 09:00 UTC. A 3-working-day window opened now ends Friday 13 March 23:59:59 IST.
    private static readonly DateTime WindowEnd = new DateTime(2026, 3, 13, 18, 30, 0, DateTimeKind.Utc).AddTicks(-10);

    public DeductionLifecycleApiTests(MySqlContainerFixture mysql)
        : base(mysql)
    {
    }

    [Fact]
    public async Task Approved_corrective_hours_on_a_reviewed_own_defect_draft_one_line_per_deliverer()
    {
        var s = await ScenarioAsync();

        var (bugId, logId) = await s.DeductibleBugAsync(hours: 4m);

        var lines = await ReworkScenario.LinesAsync(s.Manager, $"&bugId={bugId}");
        lines.Should().HaveCount(2);
        var priya = lines.Single(l => l.EmployeeId == s.Priya.Id);
        priya.Should().BeEquivalentTo(new
        {
            Status = DeductionStatus.Draft,
            Kind = DeductionKind.Standard,
            TimeLogId = logId,
            Hours = 4m,
            HourlyRateUsed = 300.00m,
            SharePercent = 75.00m,
            Amount = 900.00m,
            SourceMonth = "2026-03",
            ApplyMonth = "2026-04",
            TaskId = s.TaskId,
        });
        lines.Single(l => l.EmployeeId == s.Hana.Id).Amount.Should().Be(250.00m, "4 h × 250 × 25%");

        var drafted = await FreshDb().AuditEvents.CountAsync(a => a.EntityType == "Deduction" && a.Action == "Drafted");
        drafted.Should().Be(2);
    }

    [Fact]
    public async Task Nothing_is_drafted_before_both_approval_and_review()
    {
        var s = await ScenarioAsync();
        var bugId = await s.ReportBugAsync();
        var logId = await s.LogCorrectiveAsync(s.PriyaClient, bugId, 3m);

        await s.ReviewAsync(bugId);
        (await ReworkScenario.LinesAsync(s.Manager)).Should().BeEmpty("the hours are not approved yet");

        await s.ApproveLogAsync(logId);
        (await ReworkScenario.LinesAsync(s.Manager)).Should().HaveCount(2);
    }

    [Fact]
    public async Task No_dispute_branch_notify_then_approve_after_the_window()
    {
        var s = await ScenarioAsync();
        var (bugId, _) = await s.DeductibleBugAsync();
        var ids = (await ReworkScenario.LinesAsync(s.Manager, $"&bugId={bugId}")).Select(l => l.Id).ToArray();

        var notified = await s.NotifyAsync(ids);
        notified.Should().OnlyContain(l => l.Status == DeductionStatus.Notified && l.DisputeWindowEndsAtUtc == WindowEnd && l.DisputeWindowOpen);

        await (await s.Manager.PostJsonAsync("/api/v1/deductions/approve", new DeductionIdsRequest(ids)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "deduction.window_open");

        Clock.UtcNow = WindowEnd.AddTicks(1);
        var approved = await s.ApproveAsync(ids);
        approved.Should().OnlyContain(l => l.Status == DeductionStatus.Approved && l.ApprovedByEmployeeId == s.ManagerEmployee.Id);
    }

    [Fact]
    public async Task Dispute_upheld_with_reduction_replaces_the_line_and_is_never_redrafted()
    {
        var s = await ScenarioAsync();
        var (bugId, _) = await s.DeductibleBugAsync();
        var priyaLine = (await ReworkScenario.LinesAsync(s.PriyaClient)).Single();
        await s.NotifyAsync(priyaLine.Id);

        var disputed = await (await s.PriyaClient.PostJsonAsync($"/api/v1/deductions/{priyaLine.Id}/dispute", new RaiseDisputeRequest("Half of this was a spec change")))
            .ReadAsync<DeductionDto>();
        disputed.Status.Should().Be(DeductionStatus.Disputed);
        disputed.Dispute!.Reason.Should().Be("Half of this was a spec change");

        await (await s.PriyaClient.PostJsonAsync($"/api/v1/deductions/{priyaLine.Id}/dispute", new RaiseDisputeRequest("Second attempt at disputing")))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "deduction.already_disputed");
        await (await s.HanaClient.PostJsonAsync($"/api/v1/deductions/{priyaLine.Id}/dispute", new RaiseDisputeRequest("Disputing a colleague's line")))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await s.Manager.PostJsonAsync("/api/v1/deductions/approve", new DeductionIdsRequest([priyaLine.Id])))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "deduction.invalid_transition");

        var replacement = await (await s.Manager.PostJsonAsync(
                $"/api/v1/deductions/{priyaLine.Id}/dispute/resolve",
                new ResolveDisputeRequest(DisputeOutcome.Upheld, "Only two hours were rework", 2m, null)))
            .ReadAsync<DeductionDto>();
        replacement.Id.Should().NotBe(priyaLine.Id);
        replacement.Status.Should().Be(DeductionStatus.Resolved);
        replacement.Amount.Should().Be(450.00m, "2 h × 300 × 75%");

        var original = await (await s.PriyaClient.GetAsync(new Uri($"/api/v1/deductions/{priyaLine.Id}", UriKind.Relative))).ReadAsync<DeductionDto>();
        original.Status.Should().Be(DeductionStatus.Cancelled);
        original.Dispute!.Outcome.Should().Be(DisputeOutcome.Upheld);
        original.Dispute.ReplacementDeductionId.Should().Be(replacement.Id);

        (await s.ApproveAsync(replacement.Id)).Single().Status.Should().Be(DeductionStatus.Approved);

        // A recalculation keeps the manager's figure and does not resurrect the cancelled line.
        var afterRecalc = await (await s.Manager.PostAsync(new Uri($"/api/v1/bugs/{bugId}/deductions/recalculate", UriKind.Relative), null))
            .ReadAsync<IReadOnlyList<DeductionDto>>();
        afterRecalc.Where(l => l.EmployeeId == s.Priya.Id).Select(l => (l.Id, l.Status)).Should().BeEquivalentTo(
            [(priyaLine.Id, DeductionStatus.Cancelled), (replacement.Id, DeductionStatus.Approved)]);

        var audit = await FreshDb().AuditEvents.Where(a => a.EntityType == "Deduction" && a.EntityId == priyaLine.Id.ToString()).Select(a => a.Action).ToListAsync();
        audit.Should().ContainInOrder("Drafted", "Notified", "DisputeRaised", "DisputeResolved");
    }

    [Fact]
    public async Task Dispute_rejected_resolves_the_line_and_it_can_be_approved()
    {
        var s = await ScenarioAsync();
        await s.DeductibleBugAsync();
        var line = (await ReworkScenario.LinesAsync(s.PriyaClient)).Single();
        await s.NotifyAsync(line.Id);
        await (await s.PriyaClient.PostJsonAsync($"/api/v1/deductions/{line.Id}/dispute", new RaiseDisputeRequest("I was on leave that day"))).ShouldHaveStatusAsync(HttpStatusCode.OK);

        await (await s.Manager.PostJsonAsync($"/api/v1/deductions/{line.Id}/dispute/resolve", new ResolveDisputeRequest(DisputeOutcome.Rejected, "short", null, null)))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        await (await s.Manager.PostJsonAsync($"/api/v1/deductions/{line.Id}/dispute/resolve", new ResolveDisputeRequest(DisputeOutcome.Rejected, "Leave calendar shows you present", 1m, null)))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        var resolved = await (await s.Manager.PostJsonAsync($"/api/v1/deductions/{line.Id}/dispute/resolve", new ResolveDisputeRequest(DisputeOutcome.Rejected, "Leave calendar shows you present", null, null)))
            .ReadAsync<DeductionDto>();

        resolved.Status.Should().Be(DeductionStatus.Resolved);
        resolved.Amount.Should().Be(line.Amount);
        (await s.ApproveAsync(line.Id)).Single().Status.Should().Be(DeductionStatus.Approved);
    }

    [Fact]
    public async Task A_dispute_after_the_window_is_refused()
    {
        var s = await ScenarioAsync();
        await s.DeductibleBugAsync();
        var line = (await ReworkScenario.LinesAsync(s.PriyaClient)).Single();
        await s.NotifyAsync(line.Id);

        Clock.UtcNow = WindowEnd.AddSeconds(1);

        await (await s.PriyaClient.PostJsonAsync($"/api/v1/deductions/{line.Id}/dispute", new RaiseDisputeRequest("Too late but trying anyway")))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "deduction.window_closed");
    }

    [Fact]
    public async Task Employees_see_only_their_own_lines_and_leads_see_their_team()
    {
        var s = await ScenarioAsync();
        await s.DeductibleBugAsync();
        var all = await ReworkScenario.LinesAsync(s.Finance);
        var hanaLine = all.Single(l => l.EmployeeId == s.Hana.Id);

        (await ReworkScenario.LinesAsync(s.PriyaClient)).Should().OnlyContain(l => l.EmployeeId == s.Priya.Id).And.HaveCount(1);
        (await ReworkScenario.LinesAsync(s.PriyaClient, $"&employeeId={s.Hana.Id}")).Should().BeEmpty();
        (await ReworkScenario.LinesAsync(s.Lead)).Should().HaveCount(2);
        (await ReworkScenario.LinesAsync(s.Qa)).Should().BeEmpty();
        await (await s.PriyaClient.GetAsync(new Uri($"/api/v1/deductions/{hanaLine.Id}", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await s.Qa.GetAsync(new Uri($"/api/v1/deductions/{hanaLine.Id}", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await s.HanaClient.GetAsync(new Uri($"/api/v1/deductions/{hanaLine.Id}", UriKind.Relative))).ShouldHaveStatusAsync(HttpStatusCode.OK);
        await (await s.PriyaClient.PostAsync(new Uri($"/api/v1/deductions/{hanaLine.Id}/acknowledge", UriKind.Relative), null)).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Only_managers_drive_the_lifecycle()
    {
        var s = await ScenarioAsync();
        await s.DeductibleBugAsync();
        var line = (await ReworkScenario.LinesAsync(s.PriyaClient)).Single();

        foreach (var client in new[] { s.PriyaClient, s.Lead, s.Finance, s.Qa })
        {
            await (await client.PostJsonAsync("/api/v1/deductions/notify", new DeductionIdsRequest([line.Id]))).ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
            await (await client.PostJsonAsync("/api/v1/deductions/approve", new DeductionIdsRequest([line.Id]))).ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
            await (await client.PostJsonAsync($"/api/v1/deductions/{line.Id}/cancel", new ReasonRequest("Cancelling this one please"))).ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Bulk_requests_are_validated_and_all_or_nothing()
    {
        var s = await ScenarioAsync();
        await s.DeductibleBugAsync();
        var lines = await ReworkScenario.LinesAsync(s.Manager);
        await s.NotifyAsync(lines[0].Id);

        await (await s.Manager.PostJsonAsync("/api/v1/deductions/notify", new DeductionIdsRequest([]))).ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        await (await s.Manager.PostJsonAsync("/api/v1/deductions/notify", new DeductionIdsRequest([lines[1].Id, lines[1].Id]))).ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        await (await s.Manager.PostJsonAsync("/api/v1/deductions/notify", new DeductionIdsRequest([lines[1].Id, 999999]))).ShouldBeProblemAsync(HttpStatusCode.NotFound);
        await (await s.Manager.PostJsonAsync("/api/v1/deductions/notify", new DeductionIdsRequest([lines[0].Id, lines[1].Id])))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "deduction.invalid_transition");

        var second = await (await s.Manager.GetAsync(new Uri($"/api/v1/deductions/{lines[1].Id}", UriKind.Relative))).ReadAsync<DeductionDto>();
        second.Status.Should().Be(DeductionStatus.Draft, "the failed batch changed nothing");
    }

    [Fact]
    public async Task A_re_review_that_clears_the_deliverers_cancels_open_lines()
    {
        var s = await ScenarioAsync();
        var (bugId, _) = await s.DeductibleBugAsync();
        var ids = (await ReworkScenario.LinesAsync(s.Manager)).Select(l => l.Id).ToArray();
        await s.NotifyAsync(ids[0]);

        await s.ReviewAsync(bugId, RootCause.SpecAmbiguity, caused: true, justification: "The acceptance criteria were ambiguous");

        (await ReworkScenario.LinesAsync(s.Manager)).Should().OnlyContain(l => l.Status == DeductionStatus.Cancelled && l.CancellationReason!.Contains("re-review"));
    }

    [Fact]
    public async Task An_attribution_override_redrafts_lines_with_the_new_split()
    {
        var s = await ScenarioAsync();
        var (bugId, _) = await s.DeductibleBugAsync(hours: 4m);

        await (await s.Manager.PutJsonAsync($"/api/v1/bugs/{bugId}/attribution", new OverrideAttributionRequest(
            [new ShareInput(s.Priya.Id, 50m), new ShareInput(s.Hana.Id, 50m)], "Pair-programmed the faulty validation")))
            .ShouldHaveStatusAsync(HttpStatusCode.OK);

        var lines = await ReworkScenario.LinesAsync(s.Manager);
        lines.Where(l => l.Status == DeductionStatus.Cancelled).Should().HaveCount(2);
        lines.Where(l => l.Status == DeductionStatus.Draft).Select(l => (l.EmployeeId, l.SharePercent, l.Amount)).Should().BeEquivalentTo(
            [(s.Priya.Id, 50.00m, 600.00m), (s.Hana.Id, 50.00m, 500.00m)]);
    }

    [Fact]
    public async Task Lines_use_the_pay_rate_in_force_on_the_work_date()
    {
        var s = await ScenarioAsync();
        var (bugId, _) = await s.DeductibleBugAsync(hours: 4m);

        // A raise effective after the work date changes nothing.
        await ChangePriyaPayAsync(s, 64000m, new DateOnly(2026, 3, 11));
        var unchanged = await (await s.Manager.PostAsync(new Uri($"/api/v1/bugs/{bugId}/deductions/recalculate", UriKind.Relative), null)).ReadAsync<IReadOnlyList<DeductionDto>>();
        unchanged.Where(l => l.EmployeeId == s.Priya.Id).Should().ContainSingle().Which.HourlyRateUsed.Should().Be(300.00m);

        // A correction effective before the work date re-values the open line.
        await ChangePriyaPayAsync(s, 56000m, new DateOnly(2026, 3, 1));
        var revalued = await (await s.Manager.PostAsync(new Uri($"/api/v1/bugs/{bugId}/deductions/recalculate", UriKind.Relative), null)).ReadAsync<IReadOnlyList<DeductionDto>>();
        var priya = revalued.Where(l => l.EmployeeId == s.Priya.Id).ToList();
        priya.Should().HaveCount(2);
        priya.Single(l => l.Status == DeductionStatus.Draft).Should().BeEquivalentTo(new { HourlyRateUsed = 350.00m, Amount = 1050.00m });
    }

    [Fact]
    public async Task An_applied_line_that_stops_being_deductible_gets_a_compensating_line()
    {
        var s = await ScenarioAsync();
        var (bugId, _) = await s.DeductibleBugAsync(hours: 4m);
        var priyaLine = (await ReworkScenario.LinesAsync(s.PriyaClient)).Single();
        await MarkAppliedAsync(priyaLine.Id);

        await s.ReviewAsync(bugId, RootCause.NewRequirement, caused: false, justification: "Client added the rule after sign-off");

        var lines = await ReworkScenario.LinesAsync(s.PriyaClient);
        lines.Single(l => l.Id == priyaLine.Id).Status.Should().Be(DeductionStatus.Applied, "finalized lines are never edited");
        var compensation = lines.Single(l => l.Kind == DeductionKind.Compensating);
        compensation.Should().BeEquivalentTo(new { Amount = -900.00m, ReversesDeductionId = priyaLine.Id, Status = DeductionStatus.Approved, ApplyMonth = "2026-05" });
    }

    [Fact]
    public async Task Managers_can_compensate_an_applied_line_once()
    {
        var s = await ScenarioAsync();
        await s.DeductibleBugAsync();
        var line = (await ReworkScenario.LinesAsync(s.PriyaClient)).Single();

        await (await s.Manager.PostJsonAsync($"/api/v1/deductions/{line.Id}/compensate", new ReasonRequest("Wrongly attributed fix")))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "deduction.not_compensable");

        await MarkAppliedAsync(line.Id);
        var compensation = await (await s.Manager.PostJsonAsync($"/api/v1/deductions/{line.Id}/compensate", new ReasonRequest("Wrongly attributed fix")))
            .ReadAsync<DeductionDto>();
        compensation.Amount.Should().Be(-line.Amount);
        compensation.Note.Should().Be("Wrongly attributed fix");

        await (await s.Manager.PostJsonAsync($"/api/v1/deductions/{line.Id}/compensate", new ReasonRequest("Wrongly attributed fix")))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "deduction.already_compensated");
    }

    [Fact]
    public async Task Employees_acknowledge_their_own_notified_lines()
    {
        var s = await ScenarioAsync();
        await s.DeductibleBugAsync();
        var line = (await ReworkScenario.LinesAsync(s.PriyaClient)).Single();

        await (await s.PriyaClient.PostAsync(new Uri($"/api/v1/deductions/{line.Id}/acknowledge", UriKind.Relative), null))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "deduction.not_acknowledgeable");
        await s.NotifyAsync(line.Id);
        var acknowledged = await (await s.PriyaClient.PostAsync(new Uri($"/api/v1/deductions/{line.Id}/acknowledge", UriKind.Relative), null)).ReadAsync<DeductionDto>();

        acknowledged.AcknowledgedAtUtc.Should().Be(Clock.UtcNow);
    }

    [Fact]
    public async Task The_database_refuses_to_edit_or_delete_finalized_lines()
    {
        var s = await ScenarioAsync();
        await s.DeductibleBugAsync();
        var line = (await ReworkScenario.LinesAsync(s.PriyaClient)).Single();
        await MarkAppliedAsync(line.Id);

        var edit = () => Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Deductions SET Amount = 1 WHERE Id = {line.Id}");
        (await edit.Should().ThrowAsync<MySqlException>()).Which.Message.Should().Contain("Finalized deduction lines cannot be edited");

        var backwards = () => Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Deductions SET Status = 'Approved' WHERE Id = {line.Id}");
        (await backwards.Should().ThrowAsync<MySqlException>()).Which.Message.Should().Contain("Locked -> Applied -> Reversed");

        var delete = () => Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Deductions WHERE Id = {line.Id}");
        (await delete.Should().ThrowAsync<MySqlException>()).Which.Message.Should().Contain("never deleted");

        // Bookkeeping columns outside the money snapshot may still change (e.g. acknowledgement).
        await Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Deductions SET AcknowledgedAtUtc = UTC_TIMESTAMP() WHERE Id = {line.Id}");
    }

    [Fact]
    public async Task One_dispute_per_line_is_also_enforced_by_the_database()
    {
        var s = await ScenarioAsync();
        await s.DeductibleBugAsync();
        var line = (await ReworkScenario.LinesAsync(s.PriyaClient)).Single();
        await s.NotifyAsync(line.Id);
        await (await s.PriyaClient.PostJsonAsync($"/api/v1/deductions/{line.Id}/dispute", new RaiseDisputeRequest("I was on leave that day"))).ShouldHaveStatusAsync(HttpStatusCode.OK);

        var duplicate = () => Db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO DeductionDisputes (DeductionId, RaisedByEmployeeId, Reason, RaisedAtUtc, Version) VALUES ({line.Id}, {s.Priya.Id}, 'Another dispute', UTC_TIMESTAMP(), 0)");

        (await duplicate.Should().ThrowAsync<MySqlException>()).Which.Message.Should().Contain("Duplicate");
    }

    private Task<ReworkScenario> ScenarioAsync() => ReworkScenario.CreateAsync(Data, Api, Clock);

    private async Task ChangePriyaPayAsync(ReworkScenario s, decimal monthly, DateOnly effectiveFrom)
    {
        await using var db = FreshDb();
        var priya = await db.Employees.Include(e => e.RateHistory).SingleAsync(e => e.Id == s.Priya.Id);
        priya.ChangePay(PayMode.Monthly, monthly, null, null, effectiveFrom, Clock.Today, s.ManagerEmployee.Id, Clock.UtcNow);
        await db.SaveChangesAsync();
    }

    /// <summary>Stands in for a payroll run (built in a later slice): Approved → Locked → Applied, the only forward path.</summary>
    private async Task MarkAppliedAsync(int deductionId)
    {
        await Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Deductions SET Status = 'Approved' WHERE Id = {deductionId}");
        await Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Deductions SET Status = 'Locked' WHERE Id = {deductionId}");
        await Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Deductions SET Status = 'Applied' WHERE Id = {deductionId}");
    }
}
