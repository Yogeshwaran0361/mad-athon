using ReworkGuard.Application.Payroll;
using ReworkGuard.Application.Payslips;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Infrastructure.Pdf;

namespace ReworkGuard.Tests.Infrastructure;

public sealed class PayslipRendererTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(120)]
    public void Renders_a_pdf_for_empty_short_and_multi_page_payslips(int lines)
    {
        var items = Enumerable.Range(1, lines).Select(i => new PayrollRunItemDto(
            i, (PayrollItemKind)(i % 3), "2026-03", new DateOnly(2026, 3, 1 + (i % 28)), 100 + i, 7, "Login module: lockout after failed attempts", 450.00m, 300.00m, 150.00m, 0m, 0m)).ToList();
        var employee = new PayrollRunEmployeeDto(
            1, 5, "E0005", "Ravi Kumar", PayMode.Daily, "22 approved hours (2.5 days) in 2026-04", true, 5000m, 0m, 420m, 900m, -480m, 420m, 0m, 0m, 900m, 480m, 0m, 5480m);
        var summary = new PayslipSummaryDto(1, 5, "E0005", "Ravi Kumar", PayMode.Daily, "2026-04", 3, 2, 5000m, 0m, 0m, -480m, 0m, 900m, 480m, new DateTime(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc));

        var pdf = new QuestPayslipRenderer().Render(new PayslipDocument(new PayslipDetailDto(summary, employee, items), employee.SalaryBasis, 20m, DateTime.UtcNow));

        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        var pages = System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(pdf), @"/Type\s*/Page[^s]").Count;
        if (lines == 120)
        {
            pages.Should().BeGreaterThan(1);
        }
    }
}
