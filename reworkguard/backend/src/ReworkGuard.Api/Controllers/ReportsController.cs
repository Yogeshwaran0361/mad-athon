using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReworkGuard.Api.Authorization;
using ReworkGuard.Application.Reports;

namespace ReworkGuard.Api.Controllers;

/// <summary>
/// Rework and payroll reports. Each takes <c>format=json|xlsx|csv|pdf</c> (default json) and returns a download for the
/// file formats. Months are yyyy-MM; a range is inclusive and spans at most 36 months. Definitions: docs/REPORTS.md.
/// </summary>
[ApiController]
[Route($"{ApiRoutes.V1}/reports")]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class ReportsController : ControllerBase
{
    private readonly ReportService _reports;
    private readonly ReportExporter _exporter;

    public ReportsController(ReportService reports, ReportExporter exporter)
    {
        _reports = reports;
        _exporter = exporter;
    }

    /// <summary>Monthly hours, rework percentage, bugs and payroll totals. Team leads see their team; Employee and QA cannot use it.</summary>
    [HttpGet("rework-summary")]
    [Authorize(Roles = Roles.ReportAggregateViewers)]
    [ProducesResponseType<ReworkSummaryReport>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReworkSummary([FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? format, CancellationToken cancellationToken)
    {
        var kind = ReportExporter.ParseFormat(format);
        var report = await _reports.ReworkSummaryAsync(from, to, cancellationToken);
        return kind == ReportFormat.Json ? Ok(report) : Download(_exporter.Export(report, kind));
    }

    /// <summary>Per-employee corrective hours, deductions and disputes. Employees see only their own row; team leads their team.</summary>
    [HttpGet("by-employee")]
    [Authorize(Roles = Roles.Everyone)]
    [ProducesResponseType<EmployeeReport>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ByEmployee([FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? format, CancellationToken cancellationToken)
    {
        var kind = ReportExporter.ParseFormat(format);
        var report = await _reports.ByEmployeeAsync(from, to, cancellationToken);
        return kind == ReportFormat.Json ? Ok(report) : Download(_exporter.Export(report, kind));
    }

    /// <summary>Per-module bugs, reopens, corrective hours and deductions applied. Team leads see their team's share.</summary>
    [HttpGet("by-module")]
    [Authorize(Roles = Roles.ReportAggregateViewers)]
    [ProducesResponseType<ModuleReport>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ByModule([FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? format, CancellationToken cancellationToken)
    {
        var kind = ReportExporter.ParseFormat(format);
        var report = await _reports.ByModuleAsync(from, to, cancellationToken);
        return kind == ReportFormat.Json ? Ok(report) : Download(_exporter.Export(report, kind));
    }

    /// <summary>The month's finalized run, one row per employee. 409 <c>report.month_not_finalized</c> before finalization.</summary>
    [HttpGet("payroll-register")]
    [Authorize(Roles = Roles.PayrollViewers)]
    [ProducesResponseType<PayrollRegisterReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PayrollRegister([FromQuery] string? periodMonth, [FromQuery] string? format, CancellationToken cancellationToken)
    {
        var kind = ReportExporter.ParseFormat(format);
        var report = await _reports.PayrollRegisterAsync(periodMonth, cancellationToken);
        return kind == ReportFormat.Json ? Ok(report) : Download(_exporter.Export(report, kind));
    }

    private FileContentResult Download(ReportFile file) => File(file.Content, file.ContentType, file.FileName);
}
