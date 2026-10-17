using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Infrastructure.Persistence.Configurations;

internal sealed class WorkTaskConfiguration : IEntityTypeConfiguration<WorkTask>
{
    public void Configure(EntityTypeBuilder<WorkTask> builder)
    {
        builder.ToTable("WorkTasks", t => t.HasCheckConstraint("CK_WorkTasks_ReopenCount", "ReopenCount >= 0"));
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Title).HasMaxLength(300).IsRequired();
        builder.Property(t => t.Version).IsConcurrencyToken();
        builder.HasIndex(t => t.Status);
        builder.HasOne<ChangeRequest>().WithMany().HasForeignKey(t => t.ChangeRequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.Module).WithMany().HasForeignKey(t => t.ModuleId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Assignments).WithOne().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(t => t.Assignments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(t => t.History).WithOne().HasForeignKey(h => h.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(t => t.History).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(t => t.Bugs).WithOne(b => b.Task).HasForeignKey(b => b.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(t => t.Bugs).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(t => t.OriginalDelivererIds);
        builder.Ignore(t => t.HasUnverifiedBugs);
    }
}
