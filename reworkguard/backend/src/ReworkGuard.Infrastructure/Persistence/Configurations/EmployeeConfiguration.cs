using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Infrastructure.Persistence.Configurations;

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees", t =>
        {
            t.HasCheckConstraint("CK_Employees_PayMode_Rate",
                "(PayMode = 'Monthly' AND MonthlyGross > 0) OR (PayMode = 'Daily' AND DailyRate > 0) OR (PayMode = 'Hourly' AND HourlyRate > 0)");
        });
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Code).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Email).HasMaxLength(254).IsRequired();
        builder.Property(e => e.PasswordHash).HasMaxLength(500).IsRequired();
        builder.HasIndex(e => e.Code).IsUnique();
        builder.HasIndex(e => e.Email).IsUnique();
        builder.HasOne<Employee>().WithMany().HasForeignKey(e => e.ManagerId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(e => e.PayProfile);
        builder.HasMany(e => e.RateHistory).WithOne().HasForeignKey(r => r.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(e => e.RateHistory).HasField("_rateHistory").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
