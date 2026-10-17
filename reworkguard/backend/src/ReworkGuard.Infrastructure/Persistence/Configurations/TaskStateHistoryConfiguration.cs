using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Infrastructure.Persistence.Configurations;

internal sealed class TaskStateHistoryConfiguration : IEntityTypeConfiguration<TaskStateHistory>
{
    public void Configure(EntityTypeBuilder<TaskStateHistory> builder)
    {
        builder.ToTable("TaskStateHistory");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Reason).HasMaxLength(500);
        builder.HasIndex(h => h.TaskId);

        // The by-module report counts reopenings in a period.
        builder.HasIndex(h => new { h.ToStatus, h.ChangedAtUtc });
        builder.HasOne<Employee>().WithMany().HasForeignKey(h => h.ChangedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
