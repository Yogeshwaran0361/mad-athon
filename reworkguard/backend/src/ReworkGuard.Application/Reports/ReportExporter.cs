using FluentValidation;
using FluentValidation.Results;

namespace ReworkGuard.Application.Reports;

/// <summary>Turns a report into a <see cref="ReportTable"/> and renders it with the renderer for the requested format.</summary>
public sealed class ReportExporter
{
    private static readonly ReportColumn[] SummaryColumns =
    [
        new("Month", ReportColumnKind.Text),
        new("Regular hours", ReportColumnKind.Amount),
        new("Corrective hours", ReportColumnKind.Amount),
        new("Rework %", ReportColumnKind.Amount),
        new("Bugs reported", ReportColumnKind.WholeNumber),
        new("Bugs deductible", ReportColumnKind.WholeNumber),
        new("Deductions applied", ReportColumnKind.Amount),
        new("Refunds", ReportColumnKind.Amount),
    ];

    private static readonly ReportColumn[] EmployeeColumns =
    [
        new("Employee ID", ReportColumnKind.WholeNumber),
        new("Employee", ReportColumnKind.Text),
        new("Corrective hours", ReportColumnKind.Amount),
        new("Deductions applied", ReportColumnKind.Amount),
        new("Carried forward", ReportColumnKind.Amount),
        new("Disputes raised", ReportColumnKind.WholeNumber),
        new("Disputes upheld", ReportColumnKind.WholeNumber),
    ];

    private static readonly ReportColumn[] ModuleColumns =
    [
        new("Project", ReportColumnKind.Text),
        new("Module", ReportColumnKind.Text),
        new("Bugs", ReportColumnKind.WholeNumber),
        new("Reopens", ReportColumnKind.WholeNumber),
        new("Corrective hours", ReportColumnKind.Amount),
        new("Deduction amount", ReportColumnKind.Amount),
    ];

    private static readonly ReportColumn[] RegisterColumns =
    [
        new("Employee code", ReportColumnKind.Text),
        new("Employee", ReportColumnKind.Text),
        new("Pay mode", ReportColumnKind.Text),
        new("Salary base", ReportColumnKind.Amount),
        new("Cap amount", ReportColumnKind.Amount),
        new("Carried in", ReportColumnKind.Amount),
        new("Current due", ReportColumnKind.Amount),
        new("Refunds", ReportColumnKind.Amount),
        new("Applied", ReportColumnKind.Amount),
        new("Carried forward", ReportColumnKind.Amount),
        new("Settled manually", ReportColumnKind.Amount),
        new("Net adjustment", ReportColumnKind.Amount),
    ];

    private readonly Dictionary<ReportFormat, IReportRenderer> _renderers;

    public ReportExporter(IEnumerable<IReportRenderer> renderers)
    {
        _renderers = renderers.ToDictionary(r => r.Format);
    }

    /// <summary>Parses the format query parameter: null or empty means JSON.</summary>
    public static ReportFormat ParseFormat(string? format) => format?.Trim().ToLowerInvariant() switch
    {
        null or "" or "json" => ReportFormat.Json,
        "xlsx" => ReportFormat.Xlsx,
        "csv" => ReportFormat.Csv,
        "pdf" => ReportFormat.Pdf,
        _ => throw new ValidationException([new ValidationFailure("format", "format must be json, xlsx, csv or pdf.")]),
    };

    public ReportFile Export(ReworkSummaryReport report, ReportFormat format) => Render(
        format,
        $"rework-summary_{report.From}_{report.To}",
        new ReportTable(
            "Rework summary",
            $"{report.From} to {report.To}",
            SummaryColumns,
            report.Rows.Select(r => Cells(r.Month, r.RegularHours, r.CorrectiveHours, r.ReworkPercent, r.BugsReported, r.BugsDeductible, r.DeductionsApplied, r.Refunds)).ToList()));

    public ReportFile Export(EmployeeReport report, ReportFormat format) => Render(
        format,
        $"by-employee_{report.From}_{report.To}",
        new ReportTable(
            "Rework by employee",
            $"{report.From} to {report.To}",
            EmployeeColumns,
            report.Rows.Select(r => Cells(r.EmployeeId, r.EmployeeName, r.CorrectiveHours, r.DeductionsApplied, r.CarriedForward, r.DisputesRaised, r.DisputesUpheld)).ToList()));

    public ReportFile Export(ModuleReport report, ReportFormat format) => Render(
        format,
        $"by-module_{report.From}_{report.To}",
        new ReportTable(
            "Rework by module",
            $"{report.From} to {report.To}",
            ModuleColumns,
            report.Rows.Select(r => Cells(r.ProjectName, r.ModuleName, r.Bugs, r.Reopens, r.CorrectiveHours, r.DeductionAmount)).ToList()));

    public ReportFile Export(PayrollRegisterReport report, ReportFormat format) => Render(
        format,
        $"payroll-register_{report.PeriodMonth}",
        new ReportTable(
            "Payroll register",
            $"{report.PeriodMonth}, run #{report.RunId} (version {report.RunVersion})",
            RegisterColumns,
            report.Rows.Select(r => Cells(
                r.EmployeeCode, r.EmployeeName, r.PayMode.ToString(), r.SalaryBase, r.CapAmount, r.CarriedIn, r.CurrentDue, r.Refunds, r.Applied, r.CarriedForward, r.SettledManually, r.NetAdjustment)).ToList()));

    private static object[] Cells(params object[] cells) => cells;

    private ReportFile Render(ReportFormat format, string baseName, ReportTable table)
    {
        if (!_renderers.TryGetValue(format, out var renderer))
        {
            throw new ValidationException([new ValidationFailure("format", $"{format} export is not available.")]);
        }

        return new ReportFile($"{baseName}.{renderer.Extension}", renderer.ContentType, renderer.Render(table));
    }
}
