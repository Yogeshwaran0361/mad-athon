using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Infrastructure.Persistence.Configurations;

internal sealed class BugConfiguration : IEntityTypeConfiguration<Bug>
{
    public void Configure(EntityTypeBuilder<Bug> builder)
    {
        builder.ToTable("Bugs");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Description).HasMaxLength(4000).IsRequired();
        builder.Property(b => b.ReviewJustification).HasMaxLength(2000);
        builder.Property(b => b.Version).IsConcurrencyToken();
        builder.HasIndex(b => b.TaskId);
        builder.HasIndex(b => b.Status);

        // Reports group bugs by the month they were found.
        builder.HasIndex(b => b.FoundDate);
        builder.HasOne<Employee>().WithMany().HasForeignKey(b => b.ReportedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(b => b.ReviewedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(b => b.IsReviewed);
        builder.HasOne<Bug>().WithMany().HasForeignKey(b => b.RegressionOfBugId).OnDelete(DeleteBehavior.Restrict);
    }
}
