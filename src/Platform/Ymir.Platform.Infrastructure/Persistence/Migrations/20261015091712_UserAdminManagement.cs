using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserAdminManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "display_name_overridden",
                schema: "platform",
                table: "users",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "display_name_overridden",
                schema: "platform",
                table: "users");
        }
    }
}
