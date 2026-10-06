using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LocalCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "local_credentials",
                schema: "platform",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    password_hash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    failed_attempts = table.Column<int>(type: "int", nullable: false),
                    lockout_until = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    must_change_password = table.Column<bool>(type: "bit", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_local_credentials", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_local_credentials_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "platform",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "local_credentials",
                schema: "platform");
        }
    }
}
