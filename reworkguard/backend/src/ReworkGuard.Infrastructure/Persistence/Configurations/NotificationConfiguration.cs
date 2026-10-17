using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Title).HasMaxLength(Notification.MaxTitleLength).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(Notification.MaxBodyLength).IsRequired();
        builder.Property(n => n.Link).HasMaxLength(Notification.MaxLinkLength);
        builder.Property(n => n.EntityType).HasMaxLength(40);
        builder.Property(n => n.EntityId).HasMaxLength(40);
        builder.Ignore(n => n.IsRead);
        // Inbox queries: newest first, and the unread badge.
        builder.HasIndex(n => new { n.RecipientEmployeeId, n.Id });
        builder.HasIndex(n => new { n.RecipientEmployeeId, n.ReadAtUtc });
        builder.HasOne<Employee>().WithMany().HasForeignKey(n => n.RecipientEmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
