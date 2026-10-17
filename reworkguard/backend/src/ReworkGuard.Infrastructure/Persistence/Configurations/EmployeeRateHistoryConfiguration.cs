using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Infrastructure.Persistence.Configurations;

internal sealed class EmployeeRateHistoryConfiguration : IEntityTypeConfiguration<EmployeeRateHistory>
{
    public void Configure(EntityTypeBuilder<EmployeeRateHistory> builder)
    {
        builder.ToTable("EmployeeRateHistory", t =>
        {
            t.HasCheckConstraint("CK_EmployeeRateHistory_PayMode_Rate",
                "(PayMode = 'Monthly' AND MonthlyGross > 0) OR (PayMode = 'Daily' AND DailyRate > 0) OR (PayMode = 'Hourly' AND HourlyRate > 0)");
        });
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.EmployeeId, r.EffectiveFrom }).IsUnique();
        builder.HasOne<Employee>().WithMany().HasForeignKey(r => r.RecordedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(r => r.PayProfile);
    }
}
