using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.VibeMaker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScheduledTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source",
                schema: "vibemaker",
                table: "messages",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "scheduled_tasks",
                schema: "vibemaker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<int>(type: "int", nullable: false),
                    prompt = table.Column<string>(type: "nvarchar(max)", maxLength: 32000, nullable: false),
                    model_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    thinking_level = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    cron_expression = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    time_zone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_execution_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    last_run_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_result = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    consecutive_failures = table.Column<int>(type: "int", nullable: false),
                    lease_until = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_scheduled_tasks_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "vibemaker",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_tasks_conversation_id",
                schema: "vibemaker",
                table: "scheduled_tasks",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_tasks_status_next_run_at",
                schema: "vibemaker",
                table: "scheduled_tasks",
                columns: new[] { "status", "next_run_at" });

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_tasks_user_id",
                schema: "vibemaker",
                table: "scheduled_tasks",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scheduled_tasks",
                schema: "vibemaker");

            migrationBuilder.DropColumn(
                name: "source",
                schema: "vibemaker",
                table: "messages");
        }
    }
}
