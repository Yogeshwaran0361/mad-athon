using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Reports;

public sealed record ReworkSummaryRow(
    string Month,
    decimal RegularHours,
    decimal CorrectiveHours,
    decimal ReworkPercent,
    int BugsReported,
    int BugsDeductible,
    decimal DeductionsApplied,
    decimal Refunds);

public sealed record ReworkSummaryReport(string From, string To, IReadOnlyList<ReworkSummaryRow> Rows);

public sealed record EmployeeReportRow(
    int EmployeeId,
    string EmployeeName,
    decimal CorrectiveHours,
    decimal DeductionsApplied,
    decimal CarriedForward,
    int DisputesRaised,
    int DisputesUpheld);

public sealed record EmployeeReport(string From, string To, IReadOnlyList<EmployeeReportRow> Rows);

public sealed record ModuleReportRow(
    string ProjectName,
    string ModuleName,
    int Bugs,
    int Reopens,
    decimal CorrectiveHours,
    decimal DeductionAmount);

public sealed record ModuleReport(string From, string To, IReadOnlyList<ModuleReportRow> Rows);

public sealed record PayrollRegisterRow(
    string EmployeeCode,
    string EmployeeName,
    PayMode PayMode,
    decimal SalaryBase,
    decimal CapAmount,
    decimal CarriedIn,
    decimal CurrentDue,
    decimal Refunds,
    decimal Applied,
    decimal CarriedForward,
    decimal SettledManually,
    decimal NetAdjustment);

public sealed record PayrollRegisterReport(string PeriodMonth, int RunId, int RunVersion, IReadOnlyList<PayrollRegisterRow> Rows);

/// <summary>Export formats a report can be downloaded in, besides JSON.</summary>
public enum ReportFormat
{
    Json,
    Xlsx,
    Csv,
    Pdf,
}

public enum ReportColumnKind
{
    Text,
    WholeNumber,
    Amount,
}

public sealed record ReportColumn(string Header, ReportColumnKind Kind);

/// <summary>A report flattened to a title, typed columns and rows of cells (string, int or decimal), ready for export.</summary>
public sealed record ReportTable(string Title, string Subtitle, IReadOnlyList<ReportColumn> Columns, IReadOnlyList<IReadOnlyList<object>> Rows);

public sealed record ReportFile(string FileName, string ContentType, byte[] Content);

/// <summary>Renders a <see cref="ReportTable"/> into one export format.</summary>
public interface IReportRenderer
{
    ReportFormat Format { get; }

    string ContentType { get; }

    string Extension { get; }

    byte[] Render(ReportTable table);
}
