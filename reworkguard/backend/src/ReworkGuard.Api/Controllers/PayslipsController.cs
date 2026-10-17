using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReworkGuard.Api.Authorization;
using ReworkGuard.Application.Payslips;

namespace ReworkGuard.Api.Controllers;

/// <summary>Payslips for locked payroll runs. Employees see their own; Managers, Finance and Admin see everyone's.</summary>
[ApiController]
[Route($"{ApiRoutes.V1}/payslips")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class PayslipsController : ControllerBase
{
    private readonly PayslipService _payslips;

    public PayslipsController(PayslipService payslips)
    {
        _payslips = payslips;
    }

    [HttpGet]
    [Authorize(Roles = Roles.Everyone)]
    public Task<IReadOnlyList<PayslipSummaryDto>> List([FromQuery] int? employeeId, [FromQuery] string? periodMonth, CancellationToken cancellationToken) =>
        _payslips.ListAsync(employeeId, periodMonth, cancellationToken);

    [HttpGet("{id:long}")]
    [Authorize(Roles = Roles.Everyone)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<PayslipDetailDto> Get(long id, CancellationToken cancellationToken) => _payslips.GetAsync(id, cancellationToken);

    /// <summary>The payslip as a PDF. Every download is recorded in the audit log.</summary>
    [HttpGet("{id:long}/pdf")]
    [Authorize(Roles = Roles.Everyone)]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Pdf(long id, CancellationToken cancellationToken)
    {
        var (content, fileName) = await _payslips.PdfAsync(id, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return File(content, "application/pdf", fileName);
    }
}
