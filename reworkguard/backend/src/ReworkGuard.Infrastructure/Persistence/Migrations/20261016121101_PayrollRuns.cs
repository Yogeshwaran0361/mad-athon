using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReworkGuard.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Payroll runs: periods (one row per pay month, locked on finalize), versioned runs with their per-employee and
    /// per-line snapshots, and the link from each deduction line to the run that paid it. Adds triggers that keep run
    /// figures immutable, let finalized runs only move to Locked, keep locked months locked and forbid deleting or
    /// updating the snapshot rows.
    /// Rollback: `dotnet ef database update BackgroundJobs` drops the triggers, tables and the deduction FK. Only safe
    /// before the first run is finalized; after that, roll forward with a fix instead.
    /// </summary>
    public partial class PayrollRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PeriodMonth = table.Column<string>(type: "char(7)", fixedLength: true, maxLength: 7, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RunVersion = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InputsHash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ApprovedInputsHash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CapPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    EmployeeCount = table.Column<int>(type: "int", nullable: false),
                    TotalApplied = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    TotalCarriedForward = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    TotalRefunds = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    TotalSettledManually = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    GeneratedByEmployeeId = table.Column<int>(type: "int", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    ReviewedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ApprovedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    FinalizedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    FinalizedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    LockedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    LockedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    SupersededAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ReconciliationJson = table.Column<string>(type: "json", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRuns", x => x.Id);
                    table.CheckConstraint("CK_PayrollRuns_RunVersion", "RunVersion >= 1");
                    table.ForeignKey(
                        name: "FK_PayrollRuns_Employees_ApprovedByEmployeeId",
                        column: x => x.ApprovedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRuns_Employees_FinalizedByEmployeeId",
                        column: x => x.FinalizedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRuns_Employees_GeneratedByEmployeeId",
                        column: x => x.GeneratedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRuns_Employees_LockedByEmployeeId",
                        column: x => x.LockedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRuns_Employees_ReviewedByEmployeeId",
                        column: x => x.ReviewedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollPeriods",
                columns: table => new
                {
                    Month = table.Column<string>(type: "char(7)", fixedLength: true, maxLength: 7, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsLocked = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    FinalizedRunId = table.Column<int>(type: "int", nullable: true),
                    LockedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollPeriods", x => x.Month);
                    table.ForeignKey(
                        name: "FK_PayrollPeriods_PayrollRuns_FinalizedRunId",
                        column: x => x.FinalizedRunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollRunEmployees",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PayrollRunId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    EmployeeCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EmployeeName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PayMode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SalaryBasis = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ManualSettlement = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SalaryBase = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CapAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CarriedIn = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CurrentDue = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Refunds = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CancelledByCompensation = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Applied = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CarriedForward = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    SettledManually = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    NetAdjustment = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRunEmployees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRunEmployees_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRunEmployees_PayrollRuns_PayrollRunId",
                        column: x => x.PayrollRunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PayrollRunItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PayrollRunEmployeeId = table.Column<long>(type: "bigint", nullable: false),
                    DeductionId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OriginalApplyMonth = table.Column<string>(type: "char(7)", fixedLength: true, maxLength: 7, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Due = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Applied = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CarriedForward = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    SettledManually = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CancelledByCompensation = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRunItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRunItems_Deductions_DeductionId",
                        column: x => x.DeductionId,
                        principalTable: "Deductions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollRunItems_PayrollRunEmployees_PayrollRunEmployeeId",
                        column: x => x.PayrollRunEmployeeId,
                        principalTable: "PayrollRunEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Deductions_PayrollRunId_Status",
                table: "Deductions",
                columns: new[] { "PayrollRunId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPeriods_FinalizedRunId",
                table: "PayrollPeriods",
                column: "FinalizedRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPeriods_IsLocked_Month",
                table: "PayrollPeriods",
                columns: new[] { "IsLocked", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunEmployees_EmployeeId",
                table: "PayrollRunEmployees",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunEmployees_PayrollRunId_EmployeeId",
                table: "PayrollRunEmployees",
                columns: new[] { "PayrollRunId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunItems_DeductionId",
                table: "PayrollRunItems",
                column: "DeductionId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunItems_PayrollRunEmployeeId_DeductionId",
                table: "PayrollRunItems",
                columns: new[] { "PayrollRunEmployeeId", "DeductionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_ApprovedByEmployeeId",
                table: "PayrollRuns",
                column: "ApprovedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_FinalizedByEmployeeId",
                table: "PayrollRuns",
                column: "FinalizedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_GeneratedByEmployeeId",
                table: "PayrollRuns",
                column: "GeneratedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_LockedByEmployeeId",
                table: "PayrollRuns",
                column: "LockedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_PeriodMonth_RunVersion",
                table: "PayrollRuns",
                columns: new[] { "PeriodMonth", "RunVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_PeriodMonth_Status",
                table: "PayrollRuns",
                columns: new[] { "PeriodMonth", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_ReviewedByEmployeeId",
                table: "PayrollRuns",
                column: "ReviewedByEmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Deductions_PayrollRuns_PayrollRunId",
                table: "Deductions",
                column: "PayrollRunId",
                principalTable: "PayrollRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            foreach (var sql in PayrollGuards.Create)
            {
                migrationBuilder.Sql(sql);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in PayrollGuards.Drop)
            {
                migrationBuilder.Sql(sql);
            }

            migrationBuilder.DropForeignKey(
                name: "FK_Deductions_PayrollRuns_PayrollRunId",
                table: "Deductions");

            migrationBuilder.DropTable(
                name: "PayrollPeriods");

            migrationBuilder.DropTable(
                name: "PayrollRunItems");

            migrationBuilder.DropTable(
                name: "PayrollRunEmployees");

            migrationBuilder.DropTable(
                name: "PayrollRuns");

            migrationBuilder.DropIndex(
                name: "IX_Deductions_PayrollRunId_Status",
                table: "Deductions");
        }
    }
}
