using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReworkGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TimeLogs_CorrectiveHasBug",
                table: "TimeLogs");

            migrationBuilder.AddColumn<int>(
                name: "ChangeRequestId",
                table: "WorkTasks",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChangeRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    ModuleId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CustomerName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClientReference = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    QuotedAmountExTax = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ApprovedHours = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    ReceivedAmountExTax = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    ApprovedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    RejectionReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeRequests", x => x.Id);
                    table.CheckConstraint("CK_ChangeRequests_Amounts", "QuotedAmountExTax >= 0 AND ReceivedAmountExTax >= 0 AND ApprovedHours >= 0");
                    table.CheckConstraint("CK_ChangeRequests_ClientReference", "ClientReference <> ''");
                    table.ForeignKey(
                        name: "FK_ChangeRequests_Employees_ApprovedByEmployeeId",
                        column: x => x.ApprovedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChangeRequests_Modules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "Modules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChangeRequests_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_ChangeRequestId",
                table: "WorkTasks",
                column: "ChangeRequestId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TimeLogs_CorrectiveHasBug",
                table: "TimeLogs",
                sql: "(WorkType = 'Corrective' AND BugId IS NOT NULL) OR (WorkType IN ('Regular', 'Enhancement') AND BugId IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_ApprovedByEmployeeId",
                table: "ChangeRequests",
                column: "ApprovedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_ModuleId",
                table: "ChangeRequests",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_ProjectId",
                table: "ChangeRequests",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_Status",
                table: "ChangeRequests",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkTasks_ChangeRequests_ChangeRequestId",
                table: "WorkTasks",
                column: "ChangeRequestId",
                principalTable: "ChangeRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkTasks_ChangeRequests_ChangeRequestId",
                table: "WorkTasks");

            migrationBuilder.DropTable(
                name: "ChangeRequests");

            migrationBuilder.DropIndex(
                name: "IX_WorkTasks_ChangeRequestId",
                table: "WorkTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TimeLogs_CorrectiveHasBug",
                table: "TimeLogs");

            migrationBuilder.DropColumn(
                name: "ChangeRequestId",
                table: "WorkTasks");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TimeLogs_CorrectiveHasBug",
                table: "TimeLogs",
                sql: "(WorkType = 'Corrective' AND BugId IS NOT NULL) OR (WorkType = 'Regular' AND BugId IS NULL)");
        }
    }
}
