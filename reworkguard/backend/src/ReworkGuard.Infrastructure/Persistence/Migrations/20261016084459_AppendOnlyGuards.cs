using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReworkGuard.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Database-level enforcement that audit events and task state history are append-only:
    /// any UPDATE or DELETE is rejected, whichever client or account issues it.
    /// </summary>
    public partial class AppendOnlyGuards : Migration
    {
        private static readonly string[] Tables = ["AuditEvents", "TaskStateHistory"];

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.Sql(
                    $"CREATE TRIGGER `TR_{table}_NoUpdate` BEFORE UPDATE ON `{table}` FOR EACH ROW " +
                    $"SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '{table} is append-only: updates are not allowed';");
                migrationBuilder.Sql(
                    $"CREATE TRIGGER `TR_{table}_NoDelete` BEFORE DELETE ON `{table}` FOR EACH ROW " +
                    $"SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '{table} is append-only: deletes are not allowed';");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS `TR_{table}_NoUpdate`;");
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS `TR_{table}_NoDelete`;");
            }
        }
    }
}
