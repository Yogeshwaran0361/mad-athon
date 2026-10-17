using System.Net;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Attribution;
using ReworkGuard.Application.Bugs;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

public sealed class DefectReviewApiTests : ApiTestBase
{
    public DefectReviewApiTests(MySqlContainerFixture mysql)
        : base(mysql)
    {
    }

    [Fact]
    public async Task A_manager_marks_an_own_defect_deductible_and_the_review_is_audited()
    {
        var s = await ScenarioAsync();

        var reviewed = await (await s.Manager.PostJsonAsync($"/api/v1/bugs/{s.BugId}/review", new ReviewBugRequest(RootCause.OwnDefect, true, null))).ReadAsync<BugDto>();

        reviewed.IsDeductible.Should().BeTrue();
        reviewed.RootCause.Should().Be(RootCause.OwnDefect);
        await using var db = FreshDb();
        var audit = await db.AuditEvents.SingleAsync(a => a.Action == "Reviewed");
        audit.DetailsJson.Should().Contain("\"deductible\": true");
    }

    [Fact]
    public async Task Marking_not_caused_by_the_delivery_needs_a_justification_and_is_not_deductible()
    {
        var s = await ScenarioAsync();

        var missing = await s.Manager.PostJsonAsync($"/api/v1/bugs/{s.BugId}/review", new ReviewBugRequest(RootCause.ExternalIssue, false, "short"));
        var ok = await s.Manager.PostJsonAsync($"/api/v1/bugs/{s.BugId}/review", new ReviewBugRequest(RootCause.ExternalIssue, false, "Payment gateway changed its API without notice"));

        var problem = await missing.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().Contain("Justification");
        var bug = await ok.ReadAsync<BugDto>();
        bug.IsDeductible.Should().BeFalse();
        bug.ReviewJustification.Should().Be("Payment gateway changed its API without notice");
    }

    [Fact]
    public async Task Re_reviewing_keeps_the_previous_verdict_in_the_audit_trail()
    {
        var s = await ScenarioAsync();
        await s.Manager.PostJsonAsync($"/api/v1/bugs/{s.BugId}/review", new ReviewBugRequest(RootCause.OwnDefect, true, null));

        await (await s.Manager.PostJsonAsync($"/api/v1/bugs/{s.BugId}/review", new ReviewBugRequest(RootCause.SpecAmbiguity, true, "Spec allowed both readings of the rule"))).ShouldHaveStatusAsync(HttpStatusCode.OK);

        await using var db = FreshDb();
        var audit = await db.AuditEvents.SingleAsync(a => a.Action == "ReReviewed");
        audit.DetailsJson.Should().Contain("\"previous\": {").And.Contain("\"OwnDefect\"");
    }

    [Theory]
    [InlineData(Role.TeamLead)]
    [InlineData(Role.QA)]
    [InlineData(Role.Employee)]
    [InlineData(Role.Finance)]
    public async Task Only_managers_can_review(Role role)
    {
        var s = await ScenarioAsync();
        var client = await Api.ClientForAsync(await Data.EmployeeAsync(role));

        var response = await client.PostJsonAsync($"/api/v1/bugs/{s.BugId}/review", new ReviewBugRequest(RootCause.OwnDefect, true, null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Attribution_is_computed_from_approved_regular_hours()
    {
        var s = await ScenarioAsync();

        var attribution = await (await s.Manager.GetAsync(new Uri($"/api/v1/bugs/{s.BugId}/attribution", UriKind.Relative))).ReadAsync<AttributionDto>();

        attribution.Source.Should().Be(AttributionSource.Computed);
        attribution.Shares.Should().BeEquivalentTo([
            new AttributionShareDto(s.OwnerA.Id, s.OwnerA.Name, 6m, 75.00m),
            new AttributionShareDto(s.OwnerB.Id, s.OwnerB.Name, 2m, 25.00m),
        ]);
    }

    [Fact]
    public async Task A_manager_overrides_shares_with_justification_and_it_is_audited()
    {
        var s = await ScenarioAsync();

        var response = await s.Manager.PutJsonAsync($"/api/v1/bugs/{s.BugId}/attribution", new OverrideAttributionRequest(
            [new ShareInput(s.OwnerA.Id, 40m), new ShareInput(s.OwnerB.Id, 60m)],
            "Owner B wrote the failing validation rule"));

        var attribution = await response.ReadAsync<AttributionDto>();
        attribution.Source.Should().Be(AttributionSource.ManagerOverride);
        attribution.Shares.Select(x => (x.EmployeeId, x.SharePercent)).Should().BeEquivalentTo([(s.OwnerA.Id, 40m), (s.OwnerB.Id, 60m)]);
        attribution.OverrideJustification.Should().Be("Owner B wrote the failing validation rule");
        await using var db = FreshDb();
        var audit = await db.AuditEvents.SingleAsync(a => a.Action == "AttributionOverridden");
        audit.DetailsJson.Should().Contain("\"before\"").And.Contain("Computed");
    }

    [Fact]
    public async Task Overrides_must_total_100_and_name_original_deliverers()
    {
        var s = await ScenarioAsync();
        var outsider = await Data.EmployeeAsync(Role.Employee);

        var notHundred = await s.Manager.PutJsonAsync($"/api/v1/bugs/{s.BugId}/attribution", new OverrideAttributionRequest(
            [new ShareInput(s.OwnerA.Id, 40m), new ShareInput(s.OwnerB.Id, 50m)], "Splitting by component ownership"));
        var stranger = await s.Manager.PutJsonAsync($"/api/v1/bugs/{s.BugId}/attribution", new OverrideAttributionRequest(
            [new ShareInput(outsider.Id, 100m)], "Splitting by component ownership"));
        var noReason = await s.Manager.PutJsonAsync($"/api/v1/bugs/{s.BugId}/attribution", new OverrideAttributionRequest(
            [new ShareInput(s.OwnerA.Id, 100m)], ""));

        await notHundred.ShouldBeProblemAsync(HttpStatusCode.Conflict, "attribution.not_100");
        await stranger.ShouldBeProblemAsync(HttpStatusCode.Conflict, "attribution.not_original_deliverer");
        await noReason.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Team_leads_cannot_override_attribution()
    {
        var s = await ScenarioAsync();
        var lead = await Api.ClientForAsync(await Data.EmployeeAsync(Role.TeamLead));

        var response = await lead.PutJsonAsync($"/api/v1/bugs/{s.BugId}/attribution", new OverrideAttributionRequest(
            [new ShareInput(s.OwnerA.Id, 100m)], "I think it was all owner A"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<Scenario> ScenarioAsync()
    {
        var a = await Data.EmployeeAsync(Role.Employee);
        var b = await Data.EmployeeAsync(Role.Employee, PayMode.Daily, 2000m);
        var lead = await Data.EmployeeAsync(Role.TeamLead);
        var qa = await Data.EmployeeAsync(Role.QA);
        var manager = await Data.EmployeeAsync(Role.Manager);
        var task = await Data.DeliveredTaskAsync(45, Clock.UtcNow.AddDays(-7), lead, (a, 6m), (b, 2m));
        var reported = await (await (await Api.ClientForAsync(qa)).PostJsonAsync($"/api/v1/tasks/{task.Id}/bugs", new ReportBugRequest(BugSeverity.High, "Refund total off by one cent", Clock.Today)))
            .ReadAsync<BugReportedDto>(HttpStatusCode.Created);
        return new Scenario(reported.Bug.Id, a, b, await Api.ClientForAsync(manager));
    }

    private sealed record Scenario(int BugId, Employee OwnerA, Employee OwnerB, HttpClient Manager);
}
