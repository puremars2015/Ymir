using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.VibeMaker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OneDriveConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "onedrive_connections",
                schema: "vibemaker",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    microsoft_user_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    user_principal_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    drive_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    root_item_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    root_path = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    protected_refresh_token = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    last_error = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    connected_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_onedrive_connections", x => x.user_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "onedrive_connections",
                schema: "vibemaker");
        }
    }
}
