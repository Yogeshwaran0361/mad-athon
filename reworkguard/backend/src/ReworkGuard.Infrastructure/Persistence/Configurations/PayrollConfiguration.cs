using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Infrastructure.Persistence.Configurations;

internal sealed class PayrollPeriodConfiguration : IEntityTypeConfiguration<PayrollPeriod>
{
    public void Configure(EntityTypeBuilder<PayrollPeriod> builder)
    {
        builder.ToTable("PayrollPeriods");
        builder.HasKey(p => p.Month);
        builder.Property(p => p.Month).HasMaxLength(7).IsFixedLength();
        builder.Property(p => p.Version).IsConcurrencyToken();
        builder.HasOne<PayrollRun>().WithMany().HasForeignKey(p => p.FinalizedRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.IsLocked, p.Month });
    }
}

internal sealed class PayrollRunConfiguration : IEntityTypeConfiguration<PayrollRun>
{
    public void Configure(EntityTypeBuilder<PayrollRun> builder)
    {
        builder.ToTable("PayrollRuns", t => t.HasCheckConstraint("CK_PayrollRuns_RunVersion", "RunVersion >= 1"));
        builder.HasKey(r => r.Id);
        builder.Property(r => r.PeriodMonth).HasMaxLength(7).IsFixedLength().IsRequired();
        builder.Property(r => r.InputsHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(r => r.ApprovedInputsHash).HasMaxLength(64).IsFixedLength();
        builder.Property(r => r.CapPercent).HasPrecision(5, 2);
        builder.Property(r => r.ReconciliationJson).HasColumnType("json");
        builder.Property(r => r.Version).IsConcurrencyToken();
        builder.HasIndex(r => new { r.PeriodMonth, r.RunVersion }).IsUnique();
        builder.HasIndex(r => new { r.PeriodMonth, r.Status });
        builder.HasMany(r => r.Employees).WithOne().HasForeignKey(e => e.PayrollRunId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(r => r.Employees).UsePropertyAccessMode(PropertyAccessMode.Field);
        foreach (var actor in new[] { nameof(PayrollRun.GeneratedByEmployeeId), nameof(PayrollRun.ReviewedByEmployeeId), nameof(PayrollRun.ApprovedByEmployeeId), nameof(PayrollRun.FinalizedByEmployeeId), nameof(PayrollRun.LockedByEmployeeId) })
        {
            builder.HasOne<Employee>().WithMany().HasForeignKey(actor).OnDelete(DeleteBehavior.Restrict);
        }

        builder.Ignore(r => r.IsOpen);
    }
}

internal sealed class PayrollRunEmployeeConfiguration : IEntityTypeConfiguration<PayrollRunEmployee>
{
    public void Configure(EntityTypeBuilder<PayrollRunEmployee> builder)
    {
        builder.ToTable("PayrollRunEmployees");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EmployeeCode).HasMaxLength(20).IsRequired();
        builder.Property(e => e.EmployeeName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.SalaryBasis).HasMaxLength(300).IsRequired();
        builder.HasIndex(e => new { e.PayrollRunId, e.EmployeeId }).IsUnique();
        builder.HasIndex(e => e.EmployeeId);
        builder.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.Items).WithOne().HasForeignKey(i => i.PayrollRunEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(e => e.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PayrollRunItemConfiguration : IEntityTypeConfiguration<PayrollRunItem>
{
    public void Configure(EntityTypeBuilder<PayrollRunItem> builder)
    {
        builder.ToTable("PayrollRunItems");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.OriginalApplyMonth).HasMaxLength(7).IsFixedLength().IsRequired();
        builder.HasIndex(i => new { i.PayrollRunEmployeeId, i.DeductionId }).IsUnique();
        builder.HasIndex(i => i.DeductionId);
        builder.HasOne<Deduction>().WithMany().HasForeignKey(i => i.DeductionId).OnDelete(DeleteBehavior.Restrict);
    }
}
