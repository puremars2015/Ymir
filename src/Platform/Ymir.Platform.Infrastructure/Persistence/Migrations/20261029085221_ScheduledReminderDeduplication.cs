using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScheduledReminderDeduplication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_kind_source_id",
                schema: "platform",
                table: "notifications",
                columns: new[] { "user_id", "kind", "source_id" },
                unique: true,
                filter: "[source_id] IS NOT NULL AND [kind] = 'REMINDER'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notifications_user_id_kind_source_id",
                schema: "platform",
                table: "notifications");
        }
    }
}
