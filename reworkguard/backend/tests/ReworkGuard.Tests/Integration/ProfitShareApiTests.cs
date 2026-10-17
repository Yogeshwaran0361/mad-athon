using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.ChangeRequests;
using ReworkGuard.Application.Payslips;
using ReworkGuard.Application.ProfitShare;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

public sealed class ProfitShareApiTests : ApiTestBase
{
    private const string Base = "/api/v1/change-requests";

    public ProfitShareApiTests(MySqlContainerFixture mysql)
        : base(mysql)
    {
    }

    [Fact]
    public async Task Worked_example_closes_into_4800_and_3200_pending_awards_once()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var a = await Data.EmployeeAsync(Role.Employee, PayMode.Hourly, 400m, s.LeadEmployee.Id);
        var b = await Data.EmployeeAsync(Role.Employee, PayMode.Hourly, 400m, s.LeadEmployee.Id);
        var cr = await DeliveredRequestAsync(s, (a, 60m), (b, 40m));

        await CloseAsync(s, cr, 100_000m);

        var summary = await (await s.Finance.GetAsync(new Uri($"{Base}/{cr}/profit-share", UriKind.Relative))).ReadAsync<ProfitShareSummaryDto>();
        summary.LabourCostExTax.Should().Be(60_000m);
        summary.ProfitExTax.Should().Be(40_000m);
        summary.Pool.Should().Be(8_000m);
        summary.Awards.Select(x => (x.EmployeeId, x.Hours, x.SharePercent, x.Amount, x.Status, x.ApplyMonth))
            .Should().BeEquivalentTo([(a.Id, 60m, 60m, 4_800m, IncentiveAwardStatus.Pending, "2026-04"), (b.Id, 40m, 40m, 3_200m, IncentiveAwardStatus.Pending, "2026-04")]);

        await (await s.Finance.PostJsonAsync($"{Base}/{cr}/close", new CloseChangeRequestRequest(100_000m))).ShouldBeProblemAsync(HttpStatusCode.Conflict, "cr.invalid_transition");
        await using var db = FreshDb();
        (await db.IncentiveAwards.CountAsync(x => x.ChangeRequestId == cr)).Should().Be(2);
        (await db.AuditEvents.CountAsync(e => e.EntityType == "ChangeRequest" && e.Action == "ProfitShareSettled")).Should().Be(1);
    }

    [Fact]
    public async Task A_loss_making_request_settles_an_empty_pool_and_creates_no_awards()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var a = await Data.EmployeeAsync(Role.Employee, PayMode.Hourly, 400m, s.LeadEmployee.Id);
        var cr = await DeliveredRequestAsync(s, (a, 60m));

        await CloseAsync(s, cr, 30_000m);

        var summary = await (await s.Manager.GetAsync(new Uri($"{Base}/{cr}/profit-share", UriKind.Relative))).ReadAsync<ProfitShareSummaryDto>();
        summary.ProfitExTax.Should().Be(-6_000m);
        summary.Pool.Should().Be(0m);
        summary.Awards.Should().BeEmpty();
        (await FreshDb().Deductions.CountAsync()).Should().Be(0, "a loss never penalises anyone");
    }

    [Fact]
    public async Task Managers_cannot_decide_their_own_award_and_rejection_needs_a_reason()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var contributor = await Data.EmployeeAsync(Role.Manager, PayMode.Hourly, 400m);
        var cr = await DeliveredRequestAsync(s, (contributor, 10m), (s.Hana, 10m));
        await CloseAsync(s, cr, 50_000m);
        var awards = await (await s.Manager.GetAsync(new Uri("/api/v1/incentive-awards", UriKind.Relative))).ReadAsync<List<IncentiveAwardDto>>();
        var own = awards.Single(x => x.EmployeeId == contributor.Id);
        var hana = awards.Single(x => x.EmployeeId == s.Hana.Id);

        var self = await Api.ClientForAsync(contributor);
        await (await self.PostAsync(new Uri($"/api/v1/incentive-awards/{own.Id}/approve", UriKind.Relative), null)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "award.self_approval");
        await (await s.Manager.PostJsonAsync($"/api/v1/incentive-awards/{hana.Id}/reject", new RejectAwardRequest(""))).ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await s.HanaClient.PostAsync(new Uri($"/api/v1/incentive-awards/{hana.Id}/approve", UriKind.Relative), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await (await s.Manager.PostAsync(new Uri($"/api/v1/incentive-awards/{own.Id}/approve", UriKind.Relative), null)).ReadAsync<IncentiveAwardDto>())
            .Status.Should().Be(IncentiveAwardStatus.Approved);
    }

    [Fact]
    public async Task Approved_award_is_a_separate_earning_line_and_net_reconciles_on_the_payslip()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);

        // Priya: monthly 48,000 (300/h). 10 enhancement hours: labour 4,500, received 10,000, profit 5,500, pool 1,100.
        var cr = await DeliveredRequestAsync(s, (s.Priya, 10m));
        await CloseAsync(s, cr, 10_000m);
        var award = (await (await s.Manager.GetAsync(new Uri("/api/v1/incentive-awards", UriKind.Relative))).ReadAsync<List<IncentiveAwardDto>>()).Single();
        award.Amount.Should().Be(1_100m);
        await (await s.Manager.PostAsync(new Uri($"/api/v1/incentive-awards/{award.Id}/approve", UriKind.Relative), null)).ShouldHaveStatusAsync(HttpStatusCode.OK);

        // A deductible bug: Priya's 75% of 4 h × 300 = 900 deducted in April.
        var (bugId, _) = await s.DeductibleBugAsync(4m);
        await s.ApproveBugLinesAsync(bugId);

        var preview = await ReworkScenario.PreviewAsync(s.PriyaClient, s.Priya.Id, "2026-04");
        preview.Earnings.Should().Be(1_100m);
        preview.Applied.Should().Be(900m);
        preview.CapAmount.Should().Be(9_600m, "the cap is 20% of the salary base only");
        preview.NetPay.Should().Be(48_000m + 1_100m - 900m);
        preview.EarningLines!.Single().ChangeRequestTitle.Should().Be("Bulk export");

        await s.RunPayrollAsync("2026-04");
        var slipId = (await (await s.PriyaClient.GetAsync(new Uri("/api/v1/payslips", UriKind.Relative))).ReadAsync<List<PayslipSummaryDto>>()).Single().Id;
        var slip = await (await s.PriyaClient.GetAsync(new Uri($"/api/v1/payslips/{slipId}", UriKind.Relative))).ReadAsync<PayslipDetailDto>();

        slip.Employee.SalaryBase.Should().Be(48_000m);
        slip.Employee.Earnings.Should().Be(1_100m);
        slip.Employee.Applied.Should().Be(900m);
        slip.Employee.NetPay.Should().Be(slip.Employee.SalaryBase + slip.Employee.Earnings - slip.Employee.Applied);
        slip.Payslip.NetPay.Should().Be(48_200m);
        slip.EarningLines!.Should().ContainSingle().Which.Should().BeEquivalentTo(new { AwardId = award.Id, ChangeRequestTitle = "Bulk export", Hours = 10m, SharePercent = 100m, Amount = 1_100m });
        slip.Items.Should().OnlyContain(i => i.BugId == bugId, "deduction lines stay separate from earnings");
        (await FreshDb().IncentiveAwards.SingleAsync(x => x.Id == award.Id)).Status.Should().Be(IncentiveAwardStatus.Paid);
        (await (await s.PriyaClient.GetAsync(new Uri($"/api/v1/payslips/{slipId}/pdf", UriKind.Relative))).Content.ReadAsByteArrayAsync()).Should().NotBeEmpty();
    }

    [Fact]
    public async Task An_employee_sees_only_their_own_award_amount_never_revenue_profit_or_others()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var cr = await DeliveredRequestAsync(s, (s.Priya, 6m), (s.Hana, 4m));
        await CloseAsync(s, cr, 20_000m);

        (await s.PriyaClient.GetAsync(new Uri($"{Base}/{cr}", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.PriyaClient.GetAsync(new Uri($"{Base}/{cr}/profit-share", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.Lead.GetAsync(new Uri($"{Base}/{cr}/profit-share", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var raw = await (await s.PriyaClient.GetAsync(new Uri("/api/v1/incentive-awards", UriKind.Relative))).Content.ReadAsStringAsync();
        var mine = JsonSerializer.Deserialize<JsonElement>(raw).EnumerateArray().ToList();
        mine.Should().ContainSingle().Which.GetProperty("employeeId").GetInt32().Should().Be(s.Priya.Id);
        raw.Should().NotContainAny("received", "Received", "profit\"", "Profit\"", "labour", "Labour", "pool", "Pool", "hourlyRate", "quoted");
    }

    /// <summary>A request on an old closed task, approved for the given people, with their approved Enhancement hours, completed.</summary>
    private async Task<int> DeliveredRequestAsync(ReworkScenario s, params (Employee Who, decimal Hours)[] work)
    {
        var old = await Data.DeliveredTaskAsync(45, Clock.UtcNow.AddYears(-2), s.LeadEmployee, (s.Priya, 1m));
        var cr = await (await s.Qa.PostJsonAsync(Base, new CreateChangeRequestRequest(old.Module.ProjectId, old.ModuleId, "Bulk export", "Acme Retail", "PO-9", 100_000m)))
            .ReadAsync<ChangeRequestDto>(HttpStatusCode.Created);
        cr = await (await s.Manager.PostJsonAsync($"{Base}/{cr.Id}/approve", new ApproveChangeRequestRequest(200m, work.Select(w => w.Who.Id).ToList(), null)))
            .ReadAsync<ChangeRequestDto>();
        await (await s.Lead.PostAsync(new Uri($"{Base}/{cr.Id}/start", UriKind.Relative), null)).ShouldHaveStatusAsync(HttpStatusCode.OK);

        // Enhancement hours, at most 8 a day, approved by the lead, written through the domain to keep the test fast.
        var task = await Db.WorkTasks.Include(t => t.Assignments).SingleAsync(t => t.Id == cr.TaskId);
        var entity = await Db.ChangeRequests.AsNoTracking().SingleAsync(c => c.Id == cr.Id);
        foreach (var (who, hours) in work)
        {
            var left = hours;
            for (var day = 1; left > 0; day++)
            {
                var chunk = Math.Min(8m, left);
                var log = task.LogEnhancementHours(entity, who.Id, Clock.Today.AddDays(-day), chunk, null, 0m, Clock.UtcNow);
                log.Approve(s.LeadEmployee.Id, Clock.UtcNow);
                Db.TimeLogs.Add(log);
                left -= chunk;
            }
        }

        await Db.SaveChangesAsync();
        await (await s.Lead.PostAsync(new Uri($"{Base}/{cr.Id}/complete", UriKind.Relative), null)).ShouldHaveStatusAsync(HttpStatusCode.OK);
        return cr.Id;
    }

    private static async Task CloseAsync(ReworkScenario s, int cr, decimal received) =>
        await (await s.Finance.PostJsonAsync($"{Base}/{cr}/close", new CloseChangeRequestRequest(received))).ShouldHaveStatusAsync(HttpStatusCode.OK);
}
