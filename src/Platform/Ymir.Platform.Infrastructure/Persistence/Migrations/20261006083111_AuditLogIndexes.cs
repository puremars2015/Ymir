using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditLogIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_audit_log_action",
                schema: "platform",
                table: "audit_log",
                column: "action");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_actor",
                schema: "platform",
                table: "audit_log",
                column: "actor");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_log_action",
                schema: "platform",
                table: "audit_log");

            migrationBuilder.DropIndex(
                name: "ix_audit_log_actor",
                schema: "platform",
                table: "audit_log");
        }
    }
}
