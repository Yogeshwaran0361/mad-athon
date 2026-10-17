using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReworkGuard.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Job bookkeeping: reminder/expiry timestamps on deductions and disputes, and system-made task status changes
    /// (TaskStateHistory.ChangedByEmployeeId becomes nullable). Hangfire creates its own Hangfire_* tables at startup.
    /// Rollback: `dotnet ef database update Notifications`; it fails while system-made history rows exist, because the
    /// column cannot become NOT NULL again. Those rows are append-only by design, so keep this migration once the
    /// warranty sweep has run in production.
    /// </summary>
    public partial class BackgroundJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "ChangedByEmployeeId",
                table: "TaskStateHistory",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<DateTime>(
                name: "WindowExpiryRecordedAtUtc",
                table: "Deductions",
                type: "datetime(6)",
                precision: 6,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WindowReminderSentAtUtc",
                table: "Deductions",
                type: "datetime(6)",
                precision: 6,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderSentAtUtc",
                table: "DeductionDisputes",
                type: "datetime(6)",
                precision: 6,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WindowExpiryRecordedAtUtc",
                table: "Deductions");

            migrationBuilder.DropColumn(
                name: "WindowReminderSentAtUtc",
                table: "Deductions");

            migrationBuilder.DropColumn(
                name: "ReminderSentAtUtc",
                table: "DeductionDisputes");

            migrationBuilder.AlterColumn<int>(
                name: "ChangedByEmployeeId",
                table: "TaskStateHistory",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
