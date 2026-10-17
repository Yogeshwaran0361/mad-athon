using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Infrastructure.Persistence.Configurations;

internal sealed class DeductionConfiguration : IEntityTypeConfiguration<Deduction>
{
    public void Configure(EntityTypeBuilder<Deduction> builder)
    {
        builder.ToTable("Deductions", t =>
        {
            t.HasCheckConstraint("CK_Deductions_Hours", "Hours > 0");
            t.HasCheckConstraint("CK_Deductions_Share", "SharePercent > 0 AND SharePercent <= 100");
            t.HasCheckConstraint(
                "CK_Deductions_Amount",
                "(Kind = 'Standard' AND Amount >= 0 AND ReversesDeductionId IS NULL) OR (Kind = 'Compensating' AND Amount <= 0 AND ReversesDeductionId IS NOT NULL)");
        });
        builder.HasKey(d => d.Id);
        builder.Property(d => d.SourceMonth).HasMaxLength(7).IsFixedLength().IsRequired();
        builder.Property(d => d.ApplyMonth).HasMaxLength(7).IsFixedLength().IsRequired();
        builder.Property(d => d.Hours).HasPrecision(6, 2);
        builder.Property(d => d.SharePercent).HasPrecision(5, 2);
        builder.Property(d => d.CancellationReason).HasMaxLength(500);
        builder.Property(d => d.Note).HasMaxLength(500);
        builder.Property(d => d.Version).IsConcurrencyToken();
        builder.HasIndex(d => new { d.EmployeeId, d.ApplyMonth });
        builder.HasIndex(d => new { d.Status, d.ApplyMonth });
        builder.HasIndex(d => d.BugId);
        builder.HasIndex(d => new { d.TimeLogId, d.EmployeeId });
        builder.HasIndex(d => d.ReversesDeductionId).IsUnique();
        builder.HasOne<Employee>().WithMany().HasForeignKey(d => d.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Bug>().WithMany().HasForeignKey(d => d.BugId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TimeLog>().WithMany().HasForeignKey(d => d.TimeLogId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Deduction>().WithMany().HasForeignKey(d => d.ReversesDeductionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(d => d.ApprovedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(d => d.Dispute).WithOne().HasForeignKey<DeductionDispute>(x => x.DeductionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PayrollRun>().WithMany().HasForeignKey(d => d.PayrollRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => new { d.PayrollRunId, d.Status });
        builder.Ignore(d => d.IsFinalized);
    }
}
