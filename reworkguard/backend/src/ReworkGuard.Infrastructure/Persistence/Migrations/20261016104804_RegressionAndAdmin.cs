using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReworkGuard.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds Bugs.RegressionOfBugId (a defect introduced by an earlier fix). Rollback: `dotnet ef database update
    /// DeductionLifecycle` drops the column; regression links recorded since are lost (the audit trail keeps them).
    /// </summary>
    public partial class RegressionAndAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RegressionOfBugId",
                table: "Bugs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bugs_RegressionOfBugId",
                table: "Bugs",
                column: "RegressionOfBugId");

            migrationBuilder.AddForeignKey(
                name: "FK_Bugs_Bugs_RegressionOfBugId",
                table: "Bugs",
                column: "RegressionOfBugId",
                principalTable: "Bugs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bugs_Bugs_RegressionOfBugId",
                table: "Bugs");

            migrationBuilder.DropIndex(
                name: "IX_Bugs_RegressionOfBugId",
                table: "Bugs");

            migrationBuilder.DropColumn(
                name: "RegressionOfBugId",
                table: "Bugs");
        }
    }
}
