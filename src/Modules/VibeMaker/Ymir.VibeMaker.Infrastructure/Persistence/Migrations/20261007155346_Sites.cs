using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.VibeMaker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sites",
                schema: "vibemaker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    slug = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_path = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    spa_mode = table.Column<bool>(type: "bit", nullable: false),
                    access_mode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    current_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sites", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "site_versions",
                schema: "vibemaker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    site_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_path = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    file_count = table.Column<int>(type: "int", nullable: false),
                    total_bytes = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    error = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_site_versions_sites_site_id",
                        column: x => x.site_id,
                        principalSchema: "vibemaker",
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_site_versions_site_id",
                schema: "vibemaker",
                table: "site_versions",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "ix_sites_slug",
                schema: "vibemaker",
                table: "sites",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sites_user_id",
                schema: "vibemaker",
                table: "sites",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "site_versions",
                schema: "vibemaker");

            migrationBuilder.DropTable(
                name: "sites",
                schema: "vibemaker");
        }
    }
}
