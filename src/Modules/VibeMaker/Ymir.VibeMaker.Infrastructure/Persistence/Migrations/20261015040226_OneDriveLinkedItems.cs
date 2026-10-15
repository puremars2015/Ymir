using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.VibeMaker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OneDriveLinkedItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "onedrive_linked_items",
                schema: "vibemaker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    scope_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    drive_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    item_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    local_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    is_folder = table.Column<bool>(type: "bit", nullable: false),
                    read_only = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    last_downloaded_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    state = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    last_error = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    conflict_count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_onedrive_linked_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "onedrive_linked_files",
                schema: "vibemaker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    link_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    path = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    path_key = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    item_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    e_tag = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    local_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_onedrive_linked_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_onedrive_linked_files_onedrive_linked_items_link_id",
                        column: x => x.link_id,
                        principalSchema: "vibemaker",
                        principalTable: "onedrive_linked_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_onedrive_linked_files_link_id_path_key",
                schema: "vibemaker",
                table: "onedrive_linked_files",
                columns: new[] { "link_id", "path_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_onedrive_linked_items_user_id_scope_id_drive_id_item_id",
                schema: "vibemaker",
                table: "onedrive_linked_items",
                columns: new[] { "user_id", "scope_id", "drive_id", "item_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "onedrive_linked_files",
                schema: "vibemaker");

            migrationBuilder.DropTable(
                name: "onedrive_linked_items",
                schema: "vibemaker");
        }
    }
}
