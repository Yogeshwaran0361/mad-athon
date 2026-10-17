using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Infrastructure.Persistence.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects", t => t.HasCheckConstraint("CK_Projects_WarrantyDays", "WarrantyDays >= 0"));
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.WarrantyDays).HasDefaultValue(Project.DefaultWarrantyDays);
    }
}
