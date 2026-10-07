using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.VibeMaker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentMakeTopics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var seededAt = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
            migrationBuilder.InsertData(
                schema: "vibemaker",
                table: "make_topics",
                columns: new[] { "id", "name", "description", "instructions", "sort_order", "is_enabled", "created_at", "updated_at" },
                values: new object[,]
                {
                    {
                        new Guid("0199b7a0-0000-7000-8000-000000000003"),
                        "建立簡報",
                        "引導整理主題與每頁大綱，產生可編輯的 PowerPoint（PPTX）",
                        "先確認簡報主題與目的、聽眾、頁數或講述時間、來源資料、風格與品牌需求；已提供的資訊不要重複詢問。先提出逐頁大綱，讓使用者確認後再建立可編輯的 .pptx，不以文字檔冒充簡報。先閱讀 /opt/ymir/templates/presentation/README.md；映像預裝 python-pptx，使用 python3，無須臨時安裝工具或建立 npm 專案。預設 16:9、繁體中文，保持文字可編輯、可讀字級、一頁一重點與一致版面；不可編造數據或引用。檢查頁數、文字、圖片及版面邊界。只有最終 .pptx 放入 system prompt 指定的本次成果目錄；腳本及中間檔放在 .ymir/tmp/，除非使用者明確要求，否則不額外交付 PDF、套件設定或腳本。",
                        30,
                        true,
                        seededAt,
                        seededAt,
                    },
                    {
                        new Guid("0199b7a0-0000-7000-8000-000000000004"),
                        "建立公告 Word",
                        "依公司公告模板引導填寫主旨、正文與聯絡資訊，產生 Word（DOCX）",
                        "先確認公告目的與主旨、發布單位與日期、公告對象、正文／名單／配合事項、聯絡窗口；已提供的資訊不要重複詢問。先整理公告草稿讓使用者確認，再產生可編輯 .docx。必須先閱讀 /opt/ymir/templates/announcement/README.md，使用 /opt/ymir/templates/announcement/template.docx 及 build.py；保留公司 Logo、橫幅、標題、背景與頁尾版面，不從空白 Word 重建。依 example.json 的欄位，將確認後的資料寫到 .ymir/tmp/announcement.json，再執行 python3 /opt/ymir/templates/announcement/build.py .ymir/tmp/announcement.json <本次成果目錄>/公告.docx。sections 的段落與 items 可依公告內容增減，不要求前三名、抽獎或固定人數。不得沿用範例活動日期、姓名、信箱或得獎資訊；缺少事實先問，不編造內容。檢查最終文字、可編輯性與分頁；只有最終 .docx 放入 system prompt 指定的本次成果目錄，JSON、工具及模板不交付。",
                        40,
                        true,
                        seededAt,
                        seededAt,
                    },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "vibemaker",
                table: "make_topics",
                keyColumn: "id",
                keyValues: new object[]
                {
                    new Guid("0199b7a0-0000-7000-8000-000000000003"),
                    new Guid("0199b7a0-0000-7000-8000-000000000004"),
                });
        }
    }
}
