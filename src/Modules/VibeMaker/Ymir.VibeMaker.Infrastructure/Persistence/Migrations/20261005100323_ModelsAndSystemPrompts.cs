using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.VibeMaker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModelsAndSystemPrompts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "system_prompt",
                schema: "vibemaker",
                table: "projects",
                type: "nvarchar(max)",
                maxLength: 10000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "model_id",
                schema: "vibemaker",
                table: "conversations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "model_id",
                schema: "vibemaker",
                table: "agent_executions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "user_settings",
                schema: "vibemaker",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    system_prompt = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_settings", x => x.user_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_settings",
                schema: "vibemaker");

            migrationBuilder.DropColumn(
                name: "system_prompt",
                schema: "vibemaker",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "model_id",
                schema: "vibemaker",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "model_id",
                schema: "vibemaker",
                table: "agent_executions");
        }
    }
}
