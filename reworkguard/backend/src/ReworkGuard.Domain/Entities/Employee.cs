using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Payroll;

namespace ReworkGuard.Domain.Entities;

public class Employee
{
    /// <summary>Effective date of the pay given at creation when no other date is known.</summary>
    public static readonly DateOnly DefaultPayEffectiveFrom = new(2000, 1, 1);

    private readonly List<EmployeeRateHistory> _rateHistory = [];

    private Employee()
    {
    }

    public Employee(
        string code,
        string name,
        string email,
        Role role,
        PayMode payMode,
        decimal? monthlyGross,
        decimal? dailyRate,
        decimal? hourlyRate,
        int? managerId,
        DateOnly? payEffectiveFrom = null)
    {
        Code = code;
        Name = name;
        Email = email;
        Role = role;
        ManagerId = managerId;
        IsActive = true;
        SetPay(payMode, monthlyGross, dailyRate, hourlyRate);
        _rateHistory.Add(new EmployeeRateHistory(payEffectiveFrom ?? DefaultPayEffectiveFrom, PayProfile, recordedByEmployeeId: null, recordedAtUtc: null));
    }

    public int Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public Role Role { get; private set; }

    public PayMode PayMode { get; private set; }

    public decimal? MonthlyGross { get; private set; }

    public decimal? DailyRate { get; private set; }

    public decimal? HourlyRate { get; private set; }

    public int? ManagerId { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Consecutive failed sign-ins since the last success or lockout.</summary>
    public int FailedLoginCount { get; private set; }

    public DateTime? LockoutEndsAtUtc { get; private set; }

    public bool IsLockedOut(DateTime utcNow) => LockoutEndsAtUtc is { } end && end > utcNow;

    /// <summary>Counts a failed sign-in; the <paramref name="maxAttempts"/>-th in a row locks the account. Returns true when this attempt locked it.</summary>
    public bool RecordFailedLogin(int maxAttempts, TimeSpan lockout, DateTime utcNow)
    {
        if (IsLockedOut(utcNow))
        {
            return false;
        }

        FailedLoginCount++;
        if (FailedLoginCount < maxAttempts)
        {
            return false;
        }

        LockoutEndsAtUtc = utcNow.Add(lockout);
        FailedLoginCount = 0;
        return true;
    }

    public void RecordSuccessfulLogin()
    {
        FailedLoginCount = 0;
        LockoutEndsAtUtc = null;
    }

    public void Unlock() => RecordSuccessfulLogin();

    public PayProfile PayProfile => new(PayMode, MonthlyGross, DailyRate, HourlyRate);

    public IReadOnlyCollection<EmployeeRateHistory> RateHistory => _rateHistory;

    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));
        }

        PasswordHash = passwordHash;
    }

    /// <summary>
    /// Records new pay from <paramref name="effectiveFrom"/>. Earlier work keeps the rate that was valid when it was done.
    /// The pay shown on the employee is the entry in force on <paramref name="today"/>.
    /// </summary>
    public EmployeeRateHistory ChangePay(
        PayMode payMode,
        decimal? monthlyGross,
        decimal? dailyRate,
        decimal? hourlyRate,
        DateOnly effectiveFrom,
        DateOnly today,
        int recordedByEmployeeId,
        DateTime utcNow)
    {
        if (_rateHistory.Any(r => r.EffectiveFrom == effectiveFrom))
        {
            throw new DomainException("employee.rate_exists", $"A pay entry effective {effectiveFrom:yyyy-MM-dd} already exists.");
        }

        var pay = Validate(payMode, monthlyGross, dailyRate, hourlyRate);
        var entry = new EmployeeRateHistory(effectiveFrom, pay, recordedByEmployeeId, utcNow);
        _rateHistory.Add(entry);

        var current = Payroll.RateHistory.EffectiveOn(_rateHistory, today);
        SetPay(current.PayMode, current.MonthlyGross, current.DailyRate, current.HourlyRate);
        return entry;
    }

    public void Rename(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 200)
        {
            throw new DomainException("employee.invalid_name", "A name of 1 to 200 characters is required.");
        }

        Name = trimmed;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    public void ChangeRole(Role role, int? managerId)
    {
        Role = role;
        ManagerId = managerId;
    }

    private static PayProfile Validate(PayMode payMode, decimal? monthlyGross, decimal? dailyRate, decimal? hourlyRate)
    {
        var required = payMode switch
        {
            PayMode.Monthly => monthlyGross,
            PayMode.Daily => dailyRate,
            PayMode.Hourly => hourlyRate,
            _ => throw new ArgumentOutOfRangeException(nameof(payMode)),
        };

        if (required is null or <= 0 || !Precision.HasAtMostTwoDecimals(required.Value))
        {
            throw new DomainException("employee.pay_missing", $"A {payMode} employee needs a positive {payMode} rate with at most two decimals.");
        }

        return new PayProfile(
            payMode,
            payMode == PayMode.Monthly ? monthlyGross : null,
            payMode == PayMode.Daily ? dailyRate : null,
            payMode == PayMode.Hourly ? hourlyRate : null);
    }

    private void SetPay(PayMode payMode, decimal? monthlyGross, decimal? dailyRate, decimal? hourlyRate)
    {
        var pay = Validate(payMode, monthlyGross, dailyRate, hourlyRate);
        PayMode = pay.PayMode;
        MonthlyGross = pay.MonthlyGross;
        DailyRate = pay.DailyRate;
        HourlyRate = pay.HourlyRate;
    }
}
