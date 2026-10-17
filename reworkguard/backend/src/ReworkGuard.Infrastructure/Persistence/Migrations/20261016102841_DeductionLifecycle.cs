using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ReworkGuard.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Deduction lifecycle: status/notification/dispute columns, compensating lines, disputes, holidays and pay rate
    /// history (back-filled from each employee's current pay, effective 2000-01-01). Adds triggers that make the money
    /// columns of finalized lines immutable and forbid deleting deductions or disputes.
    /// Rollback: `dotnet ef database update AppendOnlyGuards` drops the triggers, tables and columns. Only safe before
    /// any payroll run has used these lines; after that, roll forward with a fix instead.
    /// </summary>
    public partial class DeductionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Deductions_Status",
                table: "Deductions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Deductions_Amount",
                table: "Deductions");

            migrationBuilder.AddColumn<DateTime>(
                name: "AcknowledgedAtUtc",
                table: "Deductions",
                type: "datetime(6)",
                precision: 6,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AmountSetByDispute",
                table: "Deductions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAtUtc",
                table: "Deductions",
                type: "datetime(6)",
                precision: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedByEmployeeId",
                table: "Deductions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Deductions",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "CancelledByDecision",
                table: "Deductions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAtUtc",
                table: "Deductions",
                type: "datetime(6)",
                precision: 6,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Deductions",
                type: "datetime(6)",
                precision: 6,
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "DisputeWindowEndsAtUtc",
                table: "Deductions",
                type: "datetime(6)",
                precision: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Deductions",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Standard")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "Deductions",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "NotifiedAtUtc",
                table: "Deductions",
                type: "datetime(6)",
                precision: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayrollRunId",
                table: "Deductions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReversesDeductionId",
                table: "Deductions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RoutedFromLockedMonth",
                table: "Deductions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TimeLogId",
                table: "Deductions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "WorkDate",
                table: "Deductions",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.CreateTable(
                name: "DeductionDisputes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    DeductionId = table.Column<int>(type: "int", nullable: false),
                    RaisedByEmployeeId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RaisedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    Outcome = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResolutionJustification = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResolvedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ReplacementDeductionId = table.Column<int>(type: "int", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeductionDisputes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeductionDisputes_Deductions_DeductionId",
                        column: x => x.DeductionId,
                        principalTable: "Deductions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeductionDisputes_Deductions_ReplacementDeductionId",
                        column: x => x.ReplacementDeductionId,
                        principalTable: "Deductions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeductionDisputes_Employees_RaisedByEmployeeId",
                        column: x => x.RaisedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeductionDisputes_Employees_ResolvedByEmployeeId",
                        column: x => x.ResolvedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeRateHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    PayMode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MonthlyGross = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    DailyRate = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    HourlyRate = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    RecordedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeRateHistory", x => x.Id);
                    table.CheckConstraint("CK_EmployeeRateHistory_PayMode_Rate", "(PayMode = 'Monthly' AND MonthlyGross > 0) OR (PayMode = 'Daily' AND DailyRate > 0) OR (PayMode = 'Hourly' AND HourlyRate > 0)");
                    table.ForeignKey(
                        name: "FK_EmployeeRateHistory_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeRateHistory_Employees_RecordedByEmployeeId",
                        column: x => x.RecordedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Holidays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holidays", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "PolicySettings",
                columns: new[] { "Key", "Value" },
                values: new object[,]
                {
                    { "DisputeWindowDays", "3" },
                    { "QaMissDeductible", "false" },
                    { "QaMissSharePercent", "50" },
                    { "RequireEmployeeAcknowledgement", "false" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Deductions_ApprovedByEmployeeId",
                table: "Deductions",
                column: "ApprovedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Deductions_ReversesDeductionId",
                table: "Deductions",
                column: "ReversesDeductionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Deductions_Status_ApplyMonth",
                table: "Deductions",
                columns: new[] { "Status", "ApplyMonth" });

            migrationBuilder.CreateIndex(
                name: "IX_Deductions_TimeLogId_EmployeeId",
                table: "Deductions",
                columns: new[] { "TimeLogId", "EmployeeId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Deductions_Amount",
                table: "Deductions",
                sql: "(Kind = 'Standard' AND Amount >= 0 AND ReversesDeductionId IS NULL) OR (Kind = 'Compensating' AND Amount <= 0 AND ReversesDeductionId IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_DeductionDisputes_DeductionId",
                table: "DeductionDisputes",
                column: "DeductionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeductionDisputes_RaisedByEmployeeId",
                table: "DeductionDisputes",
                column: "RaisedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_DeductionDisputes_ReplacementDeductionId",
                table: "DeductionDisputes",
                column: "ReplacementDeductionId");

            migrationBuilder.CreateIndex(
                name: "IX_DeductionDisputes_ResolvedAtUtc",
                table: "DeductionDisputes",
                column: "ResolvedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DeductionDisputes_ResolvedByEmployeeId",
                table: "DeductionDisputes",
                column: "ResolvedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeRateHistory_EmployeeId_EffectiveFrom",
                table: "EmployeeRateHistory",
                columns: new[] { "EmployeeId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeRateHistory_RecordedByEmployeeId",
                table: "EmployeeRateHistory",
                column: "RecordedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_Date",
                table: "Holidays",
                column: "Date",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Deductions_Deductions_ReversesDeductionId",
                table: "Deductions",
                column: "ReversesDeductionId",
                principalTable: "Deductions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Deductions_Employees_ApprovedByEmployeeId",
                table: "Deductions",
                column: "ApprovedByEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Deductions_TimeLogs_TimeLogId",
                table: "Deductions",
                column: "TimeLogId",
                principalTable: "TimeLogs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(
                "INSERT INTO `EmployeeRateHistory` (`EmployeeId`, `EffectiveFrom`, `PayMode`, `MonthlyGross`, `DailyRate`, `HourlyRate`) " +
                "SELECT `Id`, '2000-01-01', `PayMode`, `MonthlyGross`, `DailyRate`, `HourlyRate` FROM `Employees`;");

            foreach (var sql in DeductionGuards.Create)
            {
                migrationBuilder.Sql(sql);
            }
        }


        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in DeductionGuards.Drop)
            {
                migrationBuilder.Sql(sql);
            }

            migrationBuilder.DropForeignKey(
                name: "FK_Deductions_Deductions_ReversesDeductionId",
                table: "Deductions");

            migrationBuilder.DropForeignKey(
                name: "FK_Deductions_Employees_ApprovedByEmployeeId",
                table: "Deductions");

            migrationBuilder.DropForeignKey(
                name: "FK_Deductions_TimeLogs_TimeLogId",
                table: "Deductions");

            migrationBuilder.DropTable(
                name: "DeductionDisputes");

            migrationBuilder.DropTable(
                name: "EmployeeRateHistory");

            migrationBuilder.DropTable(
                name: "Holidays");

            migrationBuilder.DropIndex(
                name: "IX_Deductions_ApprovedByEmployeeId",
                table: "Deductions");

            migrationBuilder.DropIndex(
                name: "IX_Deductions_ReversesDeductionId",
                table: "Deductions");

            migrationBuilder.DropIndex(
                name: "IX_Deductions_Status_ApplyMonth",
                table: "Deductions");

            migrationBuilder.DropIndex(
                name: "IX_Deductions_TimeLogId_EmployeeId",
                table: "Deductions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Deductions_Amount",
                table: "Deductions");

            migrationBuilder.DeleteData(
                table: "PolicySettings",
                keyColumn: "Key",
                keyValue: "DisputeWindowDays");

            migrationBuilder.DeleteData(
                table: "PolicySettings",
                keyColumn: "Key",
                keyValue: "QaMissDeductible");

            migrationBuilder.DeleteData(
                table: "PolicySettings",
                keyColumn: "Key",
                keyValue: "QaMissSharePercent");

            migrationBuilder.DeleteData(
                table: "PolicySettings",
                keyColumn: "Key",
                keyValue: "RequireEmployeeAcknowledgement");

            migrationBuilder.DropColumn(
                name: "AcknowledgedAtUtc",
                table: "Deductions");

            migrationBuilder.DropColumn(
                name: "AmountSetByDispute",
                table: "Deductions");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "Deductions");

            migrationBuilder.DropColumn(
                name: "ApprovedByEmployeeId",
                table: "Deductions");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Deductions");

            migrationBuilder.DropColumn(
                name: "CancelledByDecision",
                table: "Deductions");

            migrationBuilder.DropColumn(
                name: "ClosedAtUtc",
                table: "Deductions");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "Deductions");

            migrationBuilder.DropColumn(
                name: "DisputeWindowEndsAtUtc",
                table: "Deductions");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Deductions");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "Deductions");

            migrationBuilder.DropColumn(
                name: "NotifiedAtUtc",
                table: "Deductions");

            migrationBuilder.DropColumn(
                name: "PayrollRunId",
                table: "Deductions");

            migrationBuilder.DropColumn(
                name: "ReversesDeductionId",
                table: "Deductions");

            migrationBuilder.DropColumn(
                name: "RoutedFromLockedMonth",
                table: "Deductions");

            migrationBuilder.DropColumn(
                name: "TimeLogId",
                table: "Deductions");

            migrationBuilder.DropColumn(
                name: "WorkDate",
                table: "Deductions");

            migrationBuilder.CreateIndex(
                name: "IX_Deductions_Status",
                table: "Deductions",
                column: "Status");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Deductions_Amount",
                table: "Deductions",
                sql: "Amount >= 0");
        }
    }
}
