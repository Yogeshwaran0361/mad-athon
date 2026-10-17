using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Infrastructure.Persistence.Configurations;

internal sealed class AttributionShareOverrideConfiguration : IEntityTypeConfiguration<AttributionShareOverride>
{
    public void Configure(EntityTypeBuilder<AttributionShareOverride> builder)
    {
        builder.ToTable("AttributionShareOverrides", t =>
            t.HasCheckConstraint("CK_AttributionShareOverrides_Share", "SharePercent > 0 AND SharePercent <= 100"));
        builder.HasKey(o => o.Id);
        builder.Property(o => o.SharePercent).HasPrecision(5, 2);
        builder.Property(o => o.Justification).HasMaxLength(2000).IsRequired();
        builder.HasIndex(o => new { o.BugId, o.EmployeeId }).IsUnique();
        builder.HasOne<Bug>().WithMany().HasForeignKey(o => o.BugId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(o => o.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(o => o.SetByEmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
