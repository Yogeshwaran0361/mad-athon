using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Infrastructure.Persistence.Configurations;

internal sealed class PolicySettingConfiguration : IEntityTypeConfiguration<PolicySetting>
{
    public void Configure(EntityTypeBuilder<PolicySetting> builder)
    {
        builder.ToTable("PolicySettings");
        builder.HasKey(p => p.Key);
        builder.Property(p => p.Key).HasMaxLength(100);
        builder.Property(p => p.Value).HasMaxLength(200).IsRequired();
        builder.HasData(
            new PolicySetting(PolicySetting.StandardHoursPerMonth, "160"),
            new PolicySetting(PolicySetting.DailyHours, "8"),
            new PolicySetting(PolicySetting.DeductionCapPercent, "20"),
            new PolicySetting(PolicySetting.DefaultWarrantyDays, "45"),
            new PolicySetting(PolicySetting.DisputeWindowDays, "3"),
            new PolicySetting(PolicySetting.QaMissDeductible, "false"),
            new PolicySetting(PolicySetting.QaMissSharePercent, "50"),
            new PolicySetting(PolicySetting.RequireEmployeeAcknowledgement, "false"),
            new PolicySetting(PolicySetting.ProfitSharePercent, "20"),
            new PolicySetting(PolicySetting.LabourOverheadFactor, "1.5"));
    }
}
