using ReworkGuard.Application.Payslips;

namespace ReworkGuard.Application.Abstractions;

public interface IPayslipPdfRenderer
{
    byte[] Render(PayslipDocument document);
}
