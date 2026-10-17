using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ReworkGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfitShareAwards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Earnings",
                table: "PayrollRunEmployees",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LabourCostExTax",
                table: "ChangeRequests",
                type: "decimal(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ProfitExTax",
                table: "ChangeRequests",
                type: "decimal(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ProfitSharePool",
                table: "ChangeRequests",
                type: "decimal(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProfitShareSettledAtUtc",
                table: "ChangeRequests",
                type: "datetime(6)",
                precision: 6,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IncentiveAwards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ChangeRequestId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Hours = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    SharePercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ApplyMonth = table.Column<string>(type: "char(7)", fixedLength: true, maxLength: 7, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    DecidedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    RejectionReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PayrollRunId = table.Column<int>(type: "int", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncentiveAwards", x => x.Id);
                    table.CheckConstraint("CK_IncentiveAwards_Amount", "Amount >= 0 AND SharePercent >= 0 AND SharePercent <= 100");
                    table.ForeignKey(
                        name: "FK_IncentiveAwards_ChangeRequests_ChangeRequestId",
                        column: x => x.ChangeRequestId,
                        principalTable: "ChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IncentiveAwards_Employees_DecidedByEmployeeId",
                        column: x => x.DecidedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IncentiveAwards_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IncentiveAwards_PayrollRuns_PayrollRunId",
                        column: x => x.PayrollRunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "PolicySettings",
                columns: new[] { "Key", "Value" },
                values: new object[,]
                {
                    { "LabourOverheadFactor", "1.5" },
                    { "ProfitSharePercent", "20" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncentiveAwards_ChangeRequestId_EmployeeId",
                table: "IncentiveAwards",
                columns: new[] { "ChangeRequestId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncentiveAwards_DecidedByEmployeeId",
                table: "IncentiveAwards",
                column: "DecidedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_IncentiveAwards_EmployeeId",
                table: "IncentiveAwards",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_IncentiveAwards_PayrollRunId",
                table: "IncentiveAwards",
                column: "PayrollRunId");

            migrationBuilder.CreateIndex(
                name: "IX_IncentiveAwards_Status_ApplyMonth",
                table: "IncentiveAwards",
                columns: new[] { "Status", "ApplyMonth" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IncentiveAwards");

            migrationBuilder.DeleteData(
                table: "PolicySettings",
                keyColumn: "Key",
                keyValue: "LabourOverheadFactor");

            migrationBuilder.DeleteData(
                table: "PolicySettings",
                keyColumn: "Key",
                keyValue: "ProfitSharePercent");

            migrationBuilder.DropColumn(
                name: "Earnings",
                table: "PayrollRunEmployees");

            migrationBuilder.DropColumn(
                name: "LabourCostExTax",
                table: "ChangeRequests");

            migrationBuilder.DropColumn(
                name: "ProfitExTax",
                table: "ChangeRequests");

            migrationBuilder.DropColumn(
                name: "ProfitSharePool",
                table: "ChangeRequests");

            migrationBuilder.DropColumn(
                name: "ProfitShareSettledAtUtc",
                table: "ChangeRequests");
        }
    }
}
