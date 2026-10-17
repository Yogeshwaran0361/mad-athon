using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Infrastructure.Persistence.Configurations;

internal sealed class TimeLogConfiguration : IEntityTypeConfiguration<TimeLog>
{
    public void Configure(EntityTypeBuilder<TimeLog> builder)
    {
        builder.ToTable("TimeLogs", t =>
        {
            t.HasCheckConstraint("CK_TimeLogs_Hours", "Hours > 0 AND Hours <= 24");
            t.HasCheckConstraint("CK_TimeLogs_CorrectiveHasBug",
                "(WorkType = 'Corrective' AND BugId IS NOT NULL) OR (WorkType IN ('Regular', 'Enhancement') AND BugId IS NULL)");
        });
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Hours).HasPrecision(6, 2);
        builder.Property(l => l.Note).HasMaxLength(1000);
        builder.Property(l => l.Version).IsConcurrencyToken();
        builder.HasIndex(l => new { l.EmployeeId, l.WorkDate });
        builder.HasIndex(l => l.TaskId);
        // Approval queue (Pending) and the payroll/report scans (Approved within a date range) both seek on this.
        builder.HasIndex(l => new { l.ApprovalStatus, l.WorkDate });
        builder.HasOne(l => l.Task).WithMany().HasForeignKey(l => l.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Employee).WithMany().HasForeignKey(l => l.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Bug).WithMany().HasForeignKey(l => l.BugId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(l => l.ApprovedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
