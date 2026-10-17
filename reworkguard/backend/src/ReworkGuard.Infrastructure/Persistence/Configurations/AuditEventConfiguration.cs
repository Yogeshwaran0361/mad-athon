using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Infrastructure.Persistence.Configurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.EntityType).HasMaxLength(50).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(50).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.DetailsJson).HasColumnType("json").IsRequired();
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => a.OccurredAtUtc);

        // Set by the TR_AuditEvents_Chain trigger (see AuditChainSql); EF reads them back after insert.
        builder.Property(a => a.Sequence).HasDefaultValue(0L).ValueGeneratedOnAdd();
        builder.Property(a => a.PrevHash).HasColumnType("char(64)").HasDefaultValue(string.Empty).ValueGeneratedOnAdd();
        builder.Property(a => a.Hash).HasColumnType("char(64)").HasDefaultValue(string.Empty).ValueGeneratedOnAdd();
        builder.HasIndex(a => a.Sequence).IsUnique();
        builder.HasOne<Employee>().WithMany().HasForeignKey(a => a.ActorEmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
