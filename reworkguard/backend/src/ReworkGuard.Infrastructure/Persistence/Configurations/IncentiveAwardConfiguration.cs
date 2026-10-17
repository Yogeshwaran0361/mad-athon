using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Infrastructure.Persistence.Configurations;

internal sealed class IncentiveAwardConfiguration : IEntityTypeConfiguration<IncentiveAward>
{
    public void Configure(EntityTypeBuilder<IncentiveAward> builder)
    {
        builder.ToTable("IncentiveAwards", t =>
        {
            t.HasCheckConstraint("CK_IncentiveAwards_Amount", "Amount >= 0 AND SharePercent >= 0 AND SharePercent <= 100");
        });
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Hours).HasPrecision(10, 2);
        builder.Property(a => a.SharePercent).HasPrecision(5, 2);
        builder.Property(a => a.ApplyMonth).HasMaxLength(7).IsFixedLength().IsRequired();
        builder.Property(a => a.RejectionReason).HasMaxLength(500);
        builder.Property(a => a.Version).IsConcurrencyToken();

        // One award per contributor per change request: closing twice can never duplicate.
        builder.HasIndex(a => new { a.ChangeRequestId, a.EmployeeId }).IsUnique();
        builder.HasIndex(a => new { a.Status, a.ApplyMonth });
        builder.HasOne<ChangeRequest>().WithMany().HasForeignKey(a => a.ChangeRequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(a => a.DecidedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PayrollRun>().WithMany().HasForeignKey(a => a.PayrollRunId).OnDelete(DeleteBehavior.Restrict);
    }
}
