using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReworkGuard.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Audit hash chain: Sequence, PrevHash and Hash on every audit event, a single-row chain head and an insert trigger
    /// that chains each new entry (see <see cref="AuditChainSql"/>). Existing entries are chained in id order.
    /// Rollback: `dotnet ef database update PayrollRuns` drops the trigger, head and columns; the chain is lost, the
    /// entries are not.
    /// </summary>
    public partial class AuditHashChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Hash",
                table: "AuditEvents",
                type: "char(64)",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "PrevHash",
                table: "AuditEvents",
                type: "char(64)",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "Sequence",
                table: "AuditEvents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql(AuditChainSql.CreateHead);
            migrationBuilder.Sql(AuditChainSql.DropNoUpdate);
            foreach (var sql in AuditChainSql.Backfill)
            {
                migrationBuilder.Sql(sql);
            }

            migrationBuilder.Sql(AuditChainSql.CreateNoUpdate);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Sequence",
                table: "AuditEvents",
                column: "Sequence",
                unique: true);

            migrationBuilder.Sql(AuditChainSql.CreateTrigger);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AuditChainSql.DropTrigger);
            migrationBuilder.Sql("DROP TABLE IF EXISTS `AuditChainHead`");

            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_Sequence",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "Hash",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "PrevHash",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "AuditEvents");
        }
    }
}
