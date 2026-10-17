using System.Net;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Payslips;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

/// <summary>Payslips published by locking a run: visibility, detail, PDF download and its audit trail.</summary>
public sealed class PayslipApiTests : ApiTestBase
{
    public PayslipApiTests(MySqlContainerFixture mysql)
        : base(mysql)
    {
    }

    [Fact]
    public async Task Locking_a_run_publishes_payslips_that_employees_see_only_for_themselves()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var (bugId, _) = await s.DeductibleBugAsync(hours: 4m);
        await s.ApproveBugLinesAsync(bugId);
        var run = await ReworkScenario.StepAsync(s.Finance, await s.GenerateRunAsync("2026-04"), "review");
        (await ListAsync(s.Finance)).Should().BeEmpty("nothing is published before the run is locked");

        run = await ReworkScenario.StepAsync(s.Finance, await ReworkScenario.StepAsync(s.Finance, await ReworkScenario.StepAsync(s.Manager, run, "approve"), "finalize"), "lock");

        var mine = await ListAsync(s.PriyaClient);
        mine.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            EmployeeId = s.Priya.Id, PeriodMonth = "2026-04", PayrollRunId = run.Run.Id, RunVersion = 1, SalaryBase = 48000.00m, Applied = 900.00m, NetAdjustment = -900.00m,
        });
        (await ListAsync(s.Finance)).Should().HaveCount(run.Run.EmployeeCount);
        (await ListAsync(s.Manager, "?periodMonth=2026-05")).Should().BeEmpty();
        (await ListAsync(s.Lead)).Should().ContainSingle(p => p.EmployeeId == s.LeadEmployee.Id, "team leads see only their own pay");

        var detail = await (await s.PriyaClient.GetAsync(new Uri($"/api/v1/payslips/{mine[0].Id}", UriKind.Relative))).ReadAsync<PayslipDetailDto>();
        detail.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Kind = PayrollItemKind.Current, Due = 900.00m, Applied = 900.00m, BugId = bugId });

        var hanaSlip = (await ListAsync(s.Finance, $"?employeeId={s.Hana.Id}")).Single();
        await (await s.PriyaClient.GetAsync(new Uri($"/api/v1/payslips/{hanaSlip.Id}", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "payslip.not_yours");
        await (await s.PriyaClient.GetAsync(new Uri($"/api/v1/payslips/{hanaSlip.Id}/pdf", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await s.Lead.GetAsync(new Uri($"/api/v1/payslips?employeeId={s.Hana.Id}", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await s.Finance.GetAsync(new Uri("/api/v1/payslips/999999", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.NotFound);
        await (await s.Finance.GetAsync(new Uri("/api/v1/payslips?periodMonth=April", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.BadRequest);

        await using var db = FreshDb();
        (await db.Notifications.Where(n => n.Type == NotificationType.PayslipPublished).Select(n => n.RecipientEmployeeId).ToListAsync())
            .Should().BeEquivalentTo([s.Priya.Id, s.Hana.Id], "only employees with rework lines are notified");
        var priyaNote = await db.Notifications.SingleAsync(n => n.Type == NotificationType.PayslipPublished && n.RecipientEmployeeId == s.Priya.Id);
        priyaNote.Body.Should().Be("INR 900.00 was deducted for rework in 2026-04.");
        var hanaNote = await db.Notifications.SingleAsync(n => n.Type == NotificationType.PayslipPublished && n.RecipientEmployeeId == s.Hana.Id);
        hanaNote.Body.Should().Be("No rework adjustment was applied in 2026-04. INR 250.00 carries forward to next month under the cap.");
        (await db.OutboxMessages.CountAsync(m => m.Channel == NotificationChannel.Email && m.NotificationId == priyaNote.Id)).Should().Be(1, "payslip emails are mandatory");
    }

    [Fact]
    public async Task The_pdf_downloads_without_caching_and_each_download_is_audited()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var (bugId, _) = await s.DeductibleBugAsync(hours: 4m);
        await s.ApproveBugLinesAsync(bugId);
        await s.RunPayrollAsync("2026-04");
        var slip = (await ListAsync(s.PriyaClient)).Single();

        var response = await s.PriyaClient.GetAsync(new Uri($"/api/v1/payslips/{slip.Id}/pdf", UriKind.Relative));

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition!.FileName.Should().Be($"payslip_{s.Priya.Code}_2026-04.pdf");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(1000);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");

        await (await s.Finance.GetAsync(new Uri($"/api/v1/payslips/{slip.Id}/pdf", UriKind.Relative))).ShouldHaveStatusAsync(HttpStatusCode.OK);
        var audits = await FreshDb().AuditEvents.Where(a => a.EntityType == "Payslip" && a.EntityId == slip.Id.ToString()).Select(a => new { a.Action, a.ActorEmployeeId }).ToListAsync();
        audits.Should().BeEquivalentTo([new { Action = "Downloaded", ActorEmployeeId = (int?)s.Priya.Id }, new { Action = "Downloaded", ActorEmployeeId = (int?)s.FinanceEmployee.Id }]);
    }

    private static async Task<List<PayslipSummaryDto>> ListAsync(HttpClient who, string query = "") =>
        await (await who.GetAsync(new Uri($"/api/v1/payslips{query}", UriKind.Relative))).ReadAsync<List<PayslipSummaryDto>>();
}
