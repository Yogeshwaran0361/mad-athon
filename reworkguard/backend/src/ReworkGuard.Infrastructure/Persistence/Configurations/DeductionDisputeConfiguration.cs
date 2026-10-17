using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Infrastructure.Persistence.Configurations;

internal sealed class DeductionDisputeConfiguration : IEntityTypeConfiguration<DeductionDispute>
{
    public void Configure(EntityTypeBuilder<DeductionDispute> builder)
    {
        builder.ToTable("DeductionDisputes");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Reason).HasMaxLength(DeductionDispute.MaximumReasonLength).IsRequired();
        builder.Property(d => d.ResolutionJustification).HasMaxLength(DeductionDispute.MaximumReasonLength);
        builder.Property(d => d.Version).IsConcurrencyToken();
        // One dispute per deduction line, enforced by the database as well as the domain.
        builder.HasIndex(d => d.DeductionId).IsUnique();
        builder.HasIndex(d => d.ResolvedAtUtc);
        builder.HasOne<Employee>().WithMany().HasForeignKey(d => d.RaisedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(d => d.ResolvedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Deduction>().WithMany().HasForeignKey(d => d.ReplacementDeductionId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(d => d.IsOpen);
    }
}
