using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Notifications;
using ReworkGuard.Application.Payslips;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Infrastructure.Pdf;

/// <summary>Renders a payslip as an A4 PDF: who and which month, the cap calculation, every line and the net adjustment.</summary>
public sealed class QuestPayslipRenderer : IPayslipPdfRenderer
{
    private static readonly string[] LineHeaders = ["Kind", "Work date", "Task / bug", "Due", "Applied", "Carried fwd", "Settled by HR"];

    public byte[] Render(PayslipDocument document)
    {
        QuestPdfLicense.EnsureConfigured();
        var p = document.Detail.Payslip;
        var e = document.Detail.Employee;

        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(9.5f));

            page.Header().Column(col =>
            {
                col.Item().Text("ReworkGuard · Rework adjustment statement").FontSize(15).SemiBold();
                col.Item().Text($"Pay month {p.PeriodMonth} · payroll run {p.PayrollRunId}, version {p.RunVersion}").FontColor(Colors.Grey.Darken2);
                col.Item().PaddingTop(6).LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten1);
            });

            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(10);
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(110);
                        c.RelativeColumn();
                        c.ConstantColumn(110);
                        c.RelativeColumn();
                    });
                    Pair(t, "Employee", $"{p.EmployeeName} ({p.EmployeeCode})");
                    Pair(t, "Pay mode", p.PayMode.ToString());
                    Pair(t, "Salary base", Money(e.SalaryBase));
                    Pair(t, "Basis", document.SalaryBasis);
                    Pair(t, "Cap", $"{document.CapPercent.ToString("0.##", CultureInfo.InvariantCulture)}% = {Money(e.CapAmount)}");
                    Pair(t, "Published", NotificationTemplates.IstTime(p.PublishedAtUtc));
                });

                col.Item().Text("Lines").SemiBold().FontSize(11);
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(62);
                        c.ConstantColumn(58);
                        c.RelativeColumn(3);
                        c.ConstantColumn(66);
                        c.ConstantColumn(66);
                        c.ConstantColumn(66);
                        c.ConstantColumn(66);
                    });
                    t.Header(h =>
                    {
                        foreach (var title in LineHeaders)
                        {
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text(title).SemiBold();
                        }
                    });

                    if (document.Detail.Items.Count == 0)
                    {
                        t.Cell().ColumnSpan(7).Padding(3).Text("No rework lines this month.").Italic();
                    }

                    foreach (var i in document.Detail.Items)
                    {
                        Cell(t, Kind(i.Kind));
                        Cell(t, i.WorkDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture));
                        Cell(t, $"{i.TaskTitle} · bug #{i.BugId}" + (i.Kind == PayrollItemKind.CarriedIn ? $" (from {i.OriginalApplyMonth})" : string.Empty));
                        Amount(t, i.Due);
                        Amount(t, i.Applied);
                        Amount(t, i.CarriedForward);
                        Amount(t, i.SettledManually);
                    }
                });

                col.Item().AlignRight().Width(260).Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn();
                        c.ConstantColumn(100);
                    });
                    Total(t, "Carried in", e.CarriedIn);
                    Total(t, "Due this month", e.CurrentDue);
                    Total(t, "Cancelled by refunds", e.CancelledByCompensation);
                    Total(t, "Deducted (capped)", e.Applied);
                    Total(t, "Refunded", -e.Refunds);
                    Total(t, "Carried forward", e.CarriedForward);
                    if (e.SettledManually > 0)
                    {
                        Total(t, "Left for HR settlement", e.SettledManually);
                    }

                    t.Cell().BorderTop(0.75f).PaddingTop(3).Text("Net adjustment to pay").SemiBold();
                    t.Cell().BorderTop(0.75f).PaddingTop(3).AlignRight().Text(Signed(e.NetAdjustment)).SemiBold();
                });

                // Gross, + profit-share earnings, − rework deductions, = net. Separate lines, never netted.
                col.Item().PaddingTop(8).AlignRight().Width(320).Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn();
                        c.ConstantColumn(100);
                    });
                    Total(t, "Gross (salary base)", e.SalaryBase);
                    foreach (var earning in document.Detail.EarningLines ?? [])
                    {
                        Total(t, string.Create(CultureInfo.InvariantCulture, $"+ Enhancement profit share: {earning.ChangeRequestTitle} ({earning.Hours:0.##} h, {earning.SharePercent:0.00}%)"), earning.Amount);
                    }

                    Total(t, "− Rework deductions", e.Applied);
                    if (e.Refunds != 0)
                    {
                        Total(t, "+ Refunds", -e.Refunds);
                    }

                    t.Cell().BorderTop(0.75f).PaddingTop(3).Text("= Net").SemiBold();
                    t.Cell().BorderTop(0.75f).PaddingTop(3).AlignRight().Text(Money(e.NetPay)).SemiBold();
                });
            });

            page.Footer().Column(col =>
            {
                col.Item().Text(
                        "This statement covers rework deductions and refunds only. Statutory deductions and net pay are shown on the payslip from the payroll system. "
                        + "Questions or disputes: contact your manager or HR.")
                    .FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                col.Item().AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Colors.Grey.Darken1));
                    t.Span($"Generated {NotificationTemplates.IstTime(document.GeneratedAtUtc)} · page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        })).GeneratePdf();
    }

    private static string Money(decimal amount) => NotificationTemplates.Money(amount);

    private static string Signed(decimal amount) => amount > 0 ? "+" + Money(amount) : Money(amount);

    private static string Kind(PayrollItemKind kind) => kind switch
    {
        PayrollItemKind.CarriedIn => "Carried in",
        PayrollItemKind.Current => "This month",
        _ => "Refund",
    };

    private static void Pair(TableDescriptor t, string label, string value)
    {
        t.Cell().PaddingVertical(2).Text(label).FontColor(Colors.Grey.Darken2);
        t.Cell().PaddingVertical(2).Text(value);
    }

    private static void Cell(TableDescriptor t, string text) => t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(text);

    private static void Amount(TableDescriptor t, decimal value) =>
        t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).AlignRight().Text(value == 0 ? "–" : Money(value));

    private static void Total(TableDescriptor t, string label, decimal value)
    {
        t.Cell().PaddingVertical(1).Text(label);
        t.Cell().PaddingVertical(1).AlignRight().Text(Money(value));
    }
}
