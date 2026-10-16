using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.VibeMaker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserPluginSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_plugin_settings",
                schema: "vibemaker",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plugin_key = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    enabled = table.Column<bool>(type: "bit", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_plugin_settings", x => new { x.user_id, x.plugin_key });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_plugin_settings",
                schema: "vibemaker");
        }
    }
}
