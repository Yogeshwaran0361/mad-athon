using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReworkGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TimeLogs_ApprovalStatus",
                table: "TimeLogs");

            migrationBuilder.CreateIndex(
                name: "IX_TimeLogs_ApprovalStatus_WorkDate",
                table: "TimeLogs",
                columns: new[] { "ApprovalStatus", "WorkDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskStateHistory_ToStatus_ChangedAtUtc",
                table: "TaskStateHistory",
                columns: new[] { "ToStatus", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Bugs_FoundDate",
                table: "Bugs",
                column: "FoundDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TimeLogs_ApprovalStatus_WorkDate",
                table: "TimeLogs");

            migrationBuilder.DropIndex(
                name: "IX_TaskStateHistory_ToStatus_ChangedAtUtc",
                table: "TaskStateHistory");

            migrationBuilder.DropIndex(
                name: "IX_Bugs_FoundDate",
                table: "Bugs");

            migrationBuilder.CreateIndex(
                name: "IX_TimeLogs_ApprovalStatus",
                table: "TimeLogs",
                column: "ApprovalStatus");
        }
    }
}
