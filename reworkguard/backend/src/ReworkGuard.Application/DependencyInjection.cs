using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Admin;
using ReworkGuard.Application.Attribution;
using ReworkGuard.Application.Audit;
using ReworkGuard.Application.Auth;
using ReworkGuard.Application.Bugs;
using ReworkGuard.Application.Calendar;
using ReworkGuard.Application.Deductions;
using ReworkGuard.Application.Employees;
using ReworkGuard.Application.Jobs;
using ReworkGuard.Application.Notifications;
using ReworkGuard.Application.Payroll;
using ReworkGuard.Application.Payslips;
using ReworkGuard.Application.Reports;
using ReworkGuard.Application.Tasks;
using ReworkGuard.Application.TimeLogs;

namespace ReworkGuard.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddScoped<ISalaryBaseProvider, EarningsSalaryBaseProvider>();
        services.AddScoped<AuthService>();
        services.AddScoped<EmployeeQueryService>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<TaskService>();
        services.AddScoped<ChangeRequests.ChangeRequestService>();
        services.AddScoped<ProfitShare.ProfitShareService>();
        services.AddScoped<ChangeRequests.IChangeRequestClosedHandler>(sp => sp.GetRequiredService<ProfitShare.ProfitShareService>());
        services.AddScoped<BugService>();
        services.AddScoped<TimeLogService>();
        services.AddScoped<DefectReviewService>();
        services.AddScoped<AttributionResolver>();
        services.AddScoped<AttributionService>();
        services.AddScoped<PolicyProvider>();
        services.AddScoped<DeductionPreviewService>();
        services.AddScoped<PayslipService>();
        services.AddScoped<HourlyRateResolver>();
        services.AddScoped<PayrollCalendar>();
        services.AddScoped<IWorkCalendar, WorkCalendar>();
        services.AddScoped<DeductionLedger>();
        services.AddScoped<DeductionService>();
        services.AddScoped<PolicyAdminService>();
        services.AddScoped<EmployeeAdminService>();
        services.AddScoped<ProjectAdminService>();
        services.AddScoped<NotificationPublisher>();
        services.AddScoped<NotificationService>();
        services.AddScoped<OutboxProcessor>();
        services.AddScoped<OutboxDeliveryJob>();
        services.AddScoped<DeductionWindowJob>();
        services.AddScoped<WarrantySweepJob>();
        services.AddScoped<ReminderJob>();
        services.AddScoped<PayrollInputsBuilder>();
        services.AddScoped<PayrollRunService>();
        services.AddScoped<ReportService>();
        services.AddScoped<ReportExporter>();
        return services;
    }
}
