using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages", t => t.HasCheckConstraint("CK_OutboxMessages_Attempts", "`Attempts` >= 0"));
        builder.HasKey(m => m.Id);
        builder.Property(m => m.LastError).HasMaxLength(OutboxMessage.MaxErrorLength);
        builder.Property(m => m.Version).IsConcurrencyToken();
        // The delivery job scans for due pending messages.
        builder.HasIndex(m => new { m.Status, m.NextAttemptAtUtc });
        builder.HasIndex(m => new { m.NotificationId, m.Channel }).IsUnique();
        builder.HasOne(m => m.Notification).WithMany().HasForeignKey(m => m.NotificationId).OnDelete(DeleteBehavior.Restrict);
    }
}
