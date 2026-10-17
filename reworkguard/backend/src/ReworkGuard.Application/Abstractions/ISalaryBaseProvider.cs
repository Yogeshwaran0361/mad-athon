using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Payroll;

namespace ReworkGuard.Application.Abstractions;

/// <summary>An employee's salary base for a pay month and a short explanation of how it was worked out.</summary>
public sealed record SalaryBase(decimal Amount, string Basis);

/// <summary>
/// Supplies the salary base the deduction cap is a percentage of. Statutory deductions (tax, provident fund) are not
/// modelled; a provider that subtracts them can replace the default without touching the payroll engine.
/// </summary>
public interface ISalaryBaseProvider
{
    Task<IReadOnlyDictionary<int, SalaryBase>> GetBasesAsync(IReadOnlyCollection<int> employeeIds, YearMonth payMonth, DeductionPolicy policy, CancellationToken cancellationToken);
}
