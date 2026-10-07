using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.VibeMaker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlatformMcpAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mcp_server_access",
                schema: "vibemaker",
                columns: table => new
                {
                    server_name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    enabled = table.Column<bool>(type: "bit", nullable: false),
                    mode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    user_id_list = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mcp_server_access", x => x.server_name);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mcp_server_access",
                schema: "vibemaker");
        }
    }
}
