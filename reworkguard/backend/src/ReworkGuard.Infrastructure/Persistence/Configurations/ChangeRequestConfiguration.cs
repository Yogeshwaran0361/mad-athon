using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Infrastructure.Persistence.Configurations;

internal sealed class ChangeRequestConfiguration : IEntityTypeConfiguration<ChangeRequest>
{
    public void Configure(EntityTypeBuilder<ChangeRequest> builder)
    {
        builder.ToTable("ChangeRequests", t =>
        {
            t.HasCheckConstraint("CK_ChangeRequests_Amounts", "QuotedAmountExTax >= 0 AND ReceivedAmountExTax >= 0 AND ApprovedHours >= 0");
            t.HasCheckConstraint("CK_ChangeRequests_ClientReference", "ClientReference <> ''");
        });
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Title).HasMaxLength(300).IsRequired();
        builder.Property(c => c.CustomerName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.ClientReference).HasMaxLength(100).IsRequired();
        builder.Property(c => c.RejectionReason).HasMaxLength(500);
        builder.Property(c => c.ApprovedHours).HasPrecision(8, 2);
        builder.Property(c => c.Version).IsConcurrencyToken();
        builder.HasIndex(c => c.Status);
        builder.HasIndex(c => c.ProjectId);
        builder.HasOne<Project>().WithMany().HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Module>().WithMany().HasForeignKey(c => c.ModuleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(c => c.ApprovedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(c => c.AcceptsEnhancementHours);
        builder.Property(c => c.LabourCostExTax).HasPrecision(14, 2);
        builder.Property(c => c.ProfitExTax).HasPrecision(14, 2);
        builder.Property(c => c.ProfitSharePool).HasPrecision(14, 2);
    }
}
