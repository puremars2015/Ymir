using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.VibeMaker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SiteSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "site_shares",
                schema: "vibemaker",
                columns: table => new
                {
                    site_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site_shares", x => new { x.site_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_site_shares_sites_site_id",
                        column: x => x.site_id,
                        principalSchema: "vibemaker",
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "site_tickets",
                schema: "vibemaker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    site_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    token_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site_tickets", x => x.id);
                    table.ForeignKey(
                        name: "fk_site_tickets_sites_site_id",
                        column: x => x.site_id,
                        principalSchema: "vibemaker",
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_site_shares_user_id",
                schema: "vibemaker",
                table: "site_shares",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_site_tickets_expires_at",
                schema: "vibemaker",
                table: "site_tickets",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_site_tickets_site_id",
                schema: "vibemaker",
                table: "site_tickets",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "ix_site_tickets_token_hash",
                schema: "vibemaker",
                table: "site_tickets",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "site_shares",
                schema: "vibemaker");

            migrationBuilder.DropTable(
                name: "site_tickets",
                schema: "vibemaker");
        }
    }
}
