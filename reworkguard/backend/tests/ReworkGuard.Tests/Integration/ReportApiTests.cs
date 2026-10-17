using System.Net;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Deductions;
using ReworkGuard.Application.Payroll;
using ReworkGuard.Application.Reports;
using ReworkGuard.Infrastructure.Reports;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

/// <summary>
/// The reports against a seeded data set with hand-calculated figures, their role scoping, validation and exports.
///
/// Data set (clock starts Tuesday 10 March 2026; ReworkScenario cast, plus Omar, an employee outside the lead's team):
///   Task 1 (Project P1 / Module A), delivered by Priya and Hana: regular 6 h (Priya) + 2 h (Hana) on 2026-02-24.
///   Task 2 (Project P2 / Module A), delivered by Priya: regular 3 h on 2026-02-24.
///   Task 3 (Project P3 / Module A), delivered by Hana: regular 8 h on 2026-03-03.
///   Task 4 (Project P4 / Module A), delivered by Omar: regular 5 h on 2026-03-03.
///   Bug A on task 1, found 2026-03-10: Priya 4 h corrective, OwnDefect (deductible).
///     Lines: Priya 75% x 4 h x 300/h = 900; Hana 25% x 4 h x 250/h = 250. Approved, due 2026-04.
///   Bug B on task 2, found 2026-03-10: Priya 2 h corrective, NewRequirement (not deductible, no lines).
///   Bug C on task 3, found 2026-03-10: Hana 1 h corrective, OwnDefect (deductible). Line Hana 100% x 1 h x 250 = 250,
///     notified; Hana disputes on 2026-03-10, the manager upholds it (line cancelled).
///   April 2026 payroll finalized and locked (cap 20%): Priya applied 900 (cap 9,600), Hana (hourly, no April earnings)
///     applied 0 and carries 250 forward.
/// </summary>
public sealed class ReportApiTests : ApiTestBase
{
    private const string Range = "from=2026-02&to=2026-04";

    public ReportApiTests(MySqlContainerFixture mysql)
        : base(mysql)
    {
    }

    [Fact]
    public async Task Rework_summary_has_one_row_per_month_with_exact_figures()
    {
        var d = await SeedAsync();

        var report = await (await d.S.Manager.GetAsync(Url($"rework-summary?{Range}"))).ReadAsync<ReworkSummaryReport>();

        report.Rows.Should().Equal(
            // Feb: regular 6 + 2 + 3 = 11, corrective 0 -> 0%.
            new ReworkSummaryRow("2026-02", 11m, 0m, 0m, 0, 0, 0m, 0m),
            // Mar: regular 8 (Hana) + 5 (Omar) = 13; corrective 4 + 2 + 1 = 7; 7 / 20 x 100 = 35.00%.
            // Bugs A, B, C found in March; A and C are deductible (C's line was cancelled, the bug is still deductible).
            new ReworkSummaryRow("2026-03", 13m, 7m, 35.00m, 3, 2, 0m, 0m),
            // Apr: no hours or bugs; the finalized April run applied 900 (Priya) + 0 (Hana) = 900, no refunds.
            new ReworkSummaryRow("2026-04", 0m, 0m, 0m, 0, 0, 900m, 0m));
    }

    [Fact]
    public async Task By_employee_lists_people_with_activity_and_scopes_to_the_caller()
    {
        var d = await SeedAsync();
        // Priya: corrective 4 (A) + 2 (B) = 6; applied 900 in April; carries 0 out of April; no disputes.
        var priya = new EmployeeReportRow(d.S.Priya.Id, d.S.Priya.Name, 6m, 900m, 0m, 0, 0);
        // Hana: corrective 1 (C); applied 0; carries 250 out of April (the latest finalized run in range); 1 dispute, upheld.
        var hana = new EmployeeReportRow(d.S.Hana.Id, d.S.Hana.Name, 1m, 0m, 250m, 1, 1);

        var manager = await (await d.S.Manager.GetAsync(Url($"by-employee?{Range}"))).ReadAsync<EmployeeReport>();
        manager.Rows.Should().Equal(priya, hana);

        var lead = await (await d.S.Lead.GetAsync(Url($"by-employee?{Range}"))).ReadAsync<EmployeeReport>();
        lead.Rows.Should().Equal(priya, hana);

        var own = await (await d.S.PriyaClient.GetAsync(Url($"by-employee?{Range}"))).ReadAsync<EmployeeReport>();
        own.Rows.Should().Equal(priya);

        var qa = await (await d.S.Qa.GetAsync(Url($"by-employee?{Range}"))).ReadAsync<EmployeeReport>();
        qa.Rows.Should().BeEmpty("QA has no corrective hours, deductions or disputes of their own");

        // March only: no finalized run in range, so applied and carried forward are 0; Hana's dispute still counts.
        var march = await (await d.S.Manager.GetAsync(Url("by-employee?from=2026-03&to=2026-03"))).ReadAsync<EmployeeReport>();
        march.Rows.Should().Equal(priya with { DeductionsApplied = 0m }, hana with { CarriedForward = 0m });
    }

    [Fact]
    public async Task By_module_aggregates_bugs_reopens_hours_and_applied_deductions()
    {
        var d = await SeedAsync();
        var projects = await FreshDb().WorkTasks
            .Where(t => d.TaskIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Module.Project.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name);

        var report = await (await d.S.Manager.GetAsync(Url($"by-module?{Range}"))).ReadAsync<ModuleReport>();

        report.Rows.Should().BeEquivalentTo(new[]
        {
            // Task 1: bug A, reopened once, 4 h corrective; April run items for A's lines applied 900 + 0.
            new ModuleReportRow(projects[d.TaskIds[0]], "Module A", 1, 1, 4m, 900m),
            // Task 2: bug B, reopened once, 2 h corrective, not deductible.
            new ModuleReportRow(projects[d.TaskIds[1]], "Module A", 1, 1, 2m, 0m),
            // Task 3: bug C, reopened once, 1 h corrective; its only line was cancelled by the dispute.
            new ModuleReportRow(projects[d.TaskIds[2]], "Module A", 1, 1, 1m, 0m),
            // Task 4 has only regular hours, so it has no row.
        });

        var lead = await (await d.S.Lead.GetAsync(Url($"by-module?{Range}"))).ReadAsync<ModuleReport>();
        lead.Rows.Should().BeEquivalentTo(report.Rows);
    }

    [Fact]
    public async Task Team_lead_sees_team_figures_and_employee_and_qa_are_refused_aggregate_reports()
    {
        var d = await SeedAsync();

        var lead = await (await d.S.Lead.GetAsync(Url($"rework-summary?{Range}"))).ReadAsync<ReworkSummaryReport>();
        // Omar (outside the team) is left out: March regular 8, corrective 7 -> 7 / 15 x 100 = 46.666... = 46.67%.
        lead.Rows[1].Should().Be(new ReworkSummaryRow("2026-03", 8m, 7m, 46.67m, 3, 2, 0m, 0m));
        lead.Rows[2].DeductionsApplied.Should().Be(900m);

        await (await d.S.PriyaClient.GetAsync(Url($"rework-summary?{Range}"))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await d.S.Qa.GetAsync(Url($"rework-summary?{Range}"))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await d.S.PriyaClient.GetAsync(Url($"by-module?{Range}"))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await d.S.Qa.GetAsync(Url($"by-module?{Range}&format=csv"))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await d.S.Lead.GetAsync(Url("payroll-register?periodMonth=2026-04"))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await d.S.PriyaClient.GetAsync(Url("payroll-register?periodMonth=2026-04"))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Payroll_register_lists_the_finalized_run_and_refuses_unfinalized_months()
    {
        var d = await SeedAsync();

        var report = await (await d.S.Finance.GetAsync(Url("payroll-register?periodMonth=2026-04"))).ReadAsync<PayrollRegisterReport>();

        report.RunId.Should().Be(d.April.Run.Id);
        report.Rows.Should().HaveCount(d.April.Employees.Count).And.BeInAscendingOrder(r => r.EmployeeCode, StringComparer.Ordinal);
        // Priya: 48,000 monthly, cap 20% = 9,600; due 900, all applied; net -900.
        report.Rows.Single(r => r.EmployeeCode == d.S.Priya.Code).Should().Be(new PayrollRegisterRow(
            d.S.Priya.Code, d.S.Priya.Name, PayMode.Monthly, 48000m, 9600m, 0m, 900m, 0m, 900m, 0m, 0m, -900m));
        var hana = d.April.Employees.Single(e => e.EmployeeId == d.S.Hana.Id);
        report.Rows.Single(r => r.EmployeeCode == d.S.Hana.Code).Should().BeEquivalentTo(new
        {
            PayMode = PayMode.Hourly, hana.SalaryBase, hana.CapAmount, CarriedIn = 0m, CurrentDue = 250m, Refunds = 0m, Applied = 0m, CarriedForward = 250m, hana.SettledManually, hana.NetAdjustment,
        });
        (await (await d.S.Manager.GetAsync(Url("payroll-register?periodMonth=2026-04"))).ReadAsync<PayrollRegisterReport>()).Rows.Should().HaveCount(report.Rows.Count);

        await (await d.S.Finance.GetAsync(Url("payroll-register?periodMonth=2026-05"))).ShouldBeProblemAsync(HttpStatusCode.Conflict, "report.month_not_finalized");
        await (await d.S.Finance.GetAsync(Url("payroll-register?periodMonth=2026-5"))).ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        await (await d.S.Finance.GetAsync(Url("payroll-register"))).ShouldBeProblemAsync(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("rework-summary")]
    [InlineData("by-employee")]
    [InlineData("by-module")]
    public async Task Ranges_are_validated(string report)
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);

        foreach (var query in new[] { "from=2026-13&to=2026-12", "from=2026-1&to=2026-02", "to=2026-02", "from=2026-05&to=2026-04", "from=2023-01&to=2026-01", "from=2026-01&to=2026-02&format=docx" })
        {
            await (await s.Manager.GetAsync(Url($"{report}?{query}"))).ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        }

        // 36 months inclusive is the limit.
        await (await s.Manager.GetAsync(Url($"{report}?from=2023-01&to=2025-12"))).ShouldHaveStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_empty_range_still_has_a_row_per_month()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);

        var report = await (await s.Finance.GetAsync(Url("rework-summary?from=2025-11&to=2026-01"))).ReadAsync<ReworkSummaryReport>();

        report.Rows.Select(r => r.Month).Should().Equal("2025-11", "2025-12", "2026-01");
        report.Rows.Should().OnlyContain(r => r.RegularHours == 0m && r.ReworkPercent == 0m && r.BugsReported == 0 && r.DeductionsApplied == 0m);
    }

    [Fact]
    public async Task Xlsx_export_opens_with_the_header_and_values()
    {
        var d = await SeedAsync();

        var response = await d.S.Manager.GetAsync(Url($"rework-summary?{Range}&format=xlsx"));

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Content.Headers.ContentDisposition!.FileName.Should().Be("rework-summary_2026-02_2026-04.xlsx");
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var sheet = workbook.Worksheet(1);
        Enumerable.Range(1, 8).Select(c => sheet.Cell(1, c).GetString()).Should().Equal(
            "Month", "Regular hours", "Corrective hours", "Rework %", "Bugs reported", "Bugs deductible", "Deductions applied", "Refunds");
        sheet.Cell(3, 1).GetString().Should().Be("2026-03");
        sheet.Cell(3, 2).GetValue<decimal>().Should().Be(13m);
        sheet.Cell(3, 4).GetValue<decimal>().Should().Be(35m);
        sheet.Cell(3, 5).GetValue<int>().Should().Be(3);
        sheet.Cell(4, 7).GetValue<decimal>().Should().Be(900m);
        sheet.LastRowUsed()!.RowNumber().Should().Be(4);

        var register = await d.S.Finance.GetAsync(Url("payroll-register?periodMonth=2026-04&format=xlsx"));
        await register.ShouldHaveStatusAsync(HttpStatusCode.OK);
        register.Content.Headers.ContentDisposition!.FileName.Should().Be("payroll-register_2026-04.xlsx");
        using var registerBook = new XLWorkbook(new MemoryStream(await register.Content.ReadAsByteArrayAsync()));
        var row = registerBook.Worksheet(1).RowsUsed().Single(r => r.Cell(1).GetString() == d.S.Priya.Code);
        row.Cell(9).GetValue<decimal>().Should().Be(900m);
        row.Cell(12).GetValue<decimal>().Should().Be(-900m);
    }

    [Fact]
    public async Task Csv_export_quotes_fields_and_guards_against_formula_injection()
    {
        var d = await SeedAsync();
        await RenameAsync(d.S.Priya.Id, "=HYPERLINK(\"http://x\",\"pay, now\")");
        await RenameAsync(d.S.Hana.Id, "@Hana");

        var response = await d.S.Manager.GetAsync(Url($"by-employee?{Range}&format=csv"));

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition!.FileName.Should().Be("by-employee_2026-02_2026-04.csv");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Take(3).Should().Equal(Encoding.UTF8.GetPreamble());
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        text.Should().Be(
            "Employee ID,Employee,Corrective hours,Deductions applied,Carried forward,Disputes raised,Disputes upheld\r\n"
            + $"{d.S.Priya.Id},\"'=HYPERLINK(\"\"http://x\"\",\"\"pay, now\"\")\",6.00,900.00,0.00,0,0\r\n"
            + $"{d.S.Hana.Id},'@Hana,1.00,0.00,250.00,1,1\r\n");

        CsvReportRenderer.Text("-2+3").Should().Be("'-2+3");
        CsvReportRenderer.Text("+1").Should().Be("'+1");
        CsvReportRenderer.Text("line\nbreak").Should().Be("\"line\nbreak\"");
        CsvReportRenderer.Text("plain").Should().Be("plain");
    }

    [Fact]
    public async Task Pdf_export_is_a_pdf_for_every_report()
    {
        var d = await SeedAsync();

        foreach (var (url, file) in new[]
        {
            ($"rework-summary?{Range}&format=pdf", "rework-summary_2026-02_2026-04.pdf"),
            ($"by-employee?{Range}&format=pdf", "by-employee_2026-02_2026-04.pdf"),
            ($"by-module?{Range}&format=pdf", "by-module_2026-02_2026-04.pdf"),
            ("payroll-register?periodMonth=2026-04&format=PDF", "payroll-register_2026-04.pdf"),
        })
        {
            var response = await d.S.Finance.GetAsync(Url(url));
            await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
            response.Content.Headers.ContentDisposition!.FileName.Should().Be(file);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        }
    }

    private static Uri Url(string relative) => new($"/api/v1/reports/{relative}", UriKind.Relative);

    private async Task RenameAsync(int employeeId, string name)
    {
        await using var db = FreshDb();
        var employee = await db.Employees.SingleAsync(e => e.Id == employeeId);
        employee.Rename(name);
        await db.SaveChangesAsync();
    }

    private async Task<Dataset> SeedAsync()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var omar = await Data.EmployeeAsync(Role.Employee, managerId: s.ManagerEmployee.Id);
        var task2 = await Data.DeliveredTaskAsync(45, Clock.UtcNow.AddDays(-9), s.LeadEmployee, (s.Priya, 3m));
        var march8 = new DateTime(2026, 3, 8, 9, 0, 0, DateTimeKind.Utc);
        var task3 = await Data.DeliveredTaskAsync(45, march8, s.LeadEmployee, (s.Hana, 8m));
        var task4 = await Data.DeliveredTaskAsync(45, march8, s.ManagerEmployee, (omar, 5m));

        var (bugA, _) = await s.DeductibleBugAsync(hours: 4m);

        var bugB = await s.ReportBugAsync(taskId: task2.Id);
        await s.ApproveLogAsync(await s.LogCorrectiveAsync(s.PriyaClient, bugB, 2m, taskId: task2.Id));
        await s.ReviewAsync(bugB, RootCause.NewRequirement, caused: false, justification: "The customer asked for a new export field");

        var bugC = await s.ReportBugAsync(taskId: task3.Id);
        await s.ApproveLogAsync(await s.LogCorrectiveAsync(s.HanaClient, bugC, 1m, taskId: task3.Id));
        await s.ReviewAsync(bugC);
        var lineC = (await ReworkScenario.LinesAsync(s.Manager, $"&bugId={bugC}")).Single();
        lineC.Amount.Should().Be(250m);
        await s.NotifyAsync(lineC.Id);
        await (await s.HanaClient.PostJsonAsync($"/api/v1/deductions/{lineC.Id}/dispute", new RaiseDisputeRequest("This was a spec change, not my defect")))
            .ShouldHaveStatusAsync(HttpStatusCode.OK);
        await (await s.Manager.PostJsonAsync($"/api/v1/deductions/{lineC.Id}/dispute/resolve", new ResolveDisputeRequest(DisputeOutcome.Upheld, "Spec change confirmed by the client", null, null)))
            .ShouldHaveStatusAsync(HttpStatusCode.OK);

        var linesA = await s.ApproveBugLinesAsync(bugA);
        linesA.Select(l => l.Amount).Should().BeEquivalentTo([900m, 250m]);
        var april = await s.RunPayrollAsync("2026-04");
        april.Run.Status.Should().Be(PayrollRunStatus.Locked);

        var task1 = s.TaskId;
        return new Dataset(s, [task1, task2.Id, task3.Id, task4.Id], april);
    }

    private sealed record Dataset(ReworkScenario S, int[] TaskIds, PayrollRunDetailDto April);
}
