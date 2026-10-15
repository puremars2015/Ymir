using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.VibeMaker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_tool",
                schema: "vibemaker",
                table: "sites",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "produces_tool",
                schema: "vibemaker",
                table: "make_topics",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "makes_tool",
                schema: "vibemaker",
                table: "conversations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_sites_conversation_id",
                schema: "vibemaker",
                table: "sites",
                column: "conversation_id",
                unique: true,
                filter: "[is_tool] = 1");

            // 預設的「小工具架設」主題（MakeTools 之前的種子資料）產生小工具（ADR-0029）；Admin 刪掉了就沒有影響。
            migrationBuilder.Sql("UPDATE [vibemaker].[make_topics] SET [produces_tool] = 1 WHERE [id] = '0199b7a0-0000-7000-8000-000000000001';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sites_conversation_id",
                schema: "vibemaker",
                table: "sites");

            migrationBuilder.DropColumn(
                name: "is_tool",
                schema: "vibemaker",
                table: "sites");

            migrationBuilder.DropColumn(
                name: "produces_tool",
                schema: "vibemaker",
                table: "make_topics");

            migrationBuilder.DropColumn(
                name: "makes_tool",
                schema: "vibemaker",
                table: "conversations");
        }
    }
}
