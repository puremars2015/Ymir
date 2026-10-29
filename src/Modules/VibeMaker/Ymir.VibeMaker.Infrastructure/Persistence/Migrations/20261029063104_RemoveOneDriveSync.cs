using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.VibeMaker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOneDriveSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "onedrive_sync_items",
                schema: "vibemaker");

            migrationBuilder.DropTable(
                name: "onedrive_sync_scopes",
                schema: "vibemaker");

            migrationBuilder.DropColumn(
                name: "root_item_id",
                schema: "vibemaker",
                table: "onedrive_connections");

            migrationBuilder.DropColumn(
                name: "root_path",
                schema: "vibemaker",
                table: "onedrive_connections");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "root_item_id",
                schema: "vibemaker",
                table: "onedrive_connections",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "root_path",
                schema: "vibemaker",
                table: "onedrive_connections",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "onedrive_sync_scopes",
                schema: "vibemaker",
                columns: table => new
                {
                    scope_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    conflict_count = table.Column<int>(type: "int", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    drive_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    folder_item_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    folder_path = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    last_error = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    root_item_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    state = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    upload_pending = table.Column<bool>(type: "bit", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_onedrive_sync_scopes", x => x.scope_id);
                });

            migrationBuilder.CreateTable(
                name: "onedrive_sync_items",
                schema: "vibemaker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    e_tag = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    item_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    local_modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    local_size = table.Column<long>(type: "bigint", nullable: true),
                    path = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    scope_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_onedrive_sync_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_onedrive_sync_items_onedrive_sync_scopes_scope_id",
                        column: x => x.scope_id,
                        principalSchema: "vibemaker",
                        principalTable: "onedrive_sync_scopes",
                        principalColumn: "scope_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_onedrive_sync_items_scope_id",
                schema: "vibemaker",
                table: "onedrive_sync_items",
                column: "scope_id");

            migrationBuilder.CreateIndex(
                name: "ix_onedrive_sync_scopes_upload_pending_next_attempt_at",
                schema: "vibemaker",
                table: "onedrive_sync_scopes",
                columns: new[] { "upload_pending", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_onedrive_sync_scopes_user_id",
                schema: "vibemaker",
                table: "onedrive_sync_scopes",
                column: "user_id");
        }
    }
}
