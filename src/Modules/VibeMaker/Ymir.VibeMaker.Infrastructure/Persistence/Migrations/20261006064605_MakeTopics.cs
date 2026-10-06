using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.VibeMaker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeTopics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "agent_prompt",
                schema: "vibemaker",
                table: "agent_executions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "make_topics",
                schema: "vibemaker",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    instructions = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_make_topics", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_make_topics_is_enabled_sort_order",
                schema: "vibemaker",
                table: "make_topics",
                columns: new[] { "is_enabled", "sort_order" });

            // 預設的兩個 /make 主題（使用者指定）；之後由 Admin 在網頁上管理，可修改或刪除。
            var seededAt = new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
            migrationBuilder.InsertData(
                schema: "vibemaker",
                table: "make_topics",
                columns: new[] { "id", "name", "description", "instructions", "sort_order", "is_enabled", "created_at", "updated_at" },
                values: new object[,]
                {
                    {
                        new Guid("0199b7a0-0000-7000-8000-000000000001"),
                        "小工具架設",
                        "計算機、表單、倒數計時器等單頁小工具",
                        "做成可以直接用瀏覽器開啟的單一 HTML 檔（HTML、CSS、JavaScript 寫在同一個檔案），不依賴外部套件或網路；介面使用繁體中文；完成後說明檔名與使用方式。",
                        10,
                        true,
                        seededAt,
                        seededAt,
                    },
                    {
                        new Guid("0199b7a0-0000-7000-8000-000000000002"),
                        "網站系統架設",
                        "有多個頁面、資料儲存或登入功能的網站或系統",
                        "先整理功能清單與頁面結構，再規劃前端、後端與資料結構，確認後依序建立專案檔案；提供啟動方式（README）；介面使用繁體中文。",
                        20,
                        true,
                        seededAt,
                        seededAt,
                    },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "make_topics",
                schema: "vibemaker");

            migrationBuilder.DropColumn(
                name: "agent_prompt",
                schema: "vibemaker",
                table: "agent_executions");
        }
    }
}
