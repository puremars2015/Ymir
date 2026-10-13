using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ymir.VibeMaker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RagGuideMakeTopic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-0014：向量資料庫（每專案一份 SQLite）由平台在專案頁的知識庫建立，Agent 只負責引導，
            // 所以指示明確禁止 Agent 自己建 SQLite / 呼叫 embedding；管理員仍可在「管理 → Make 主題」調整或停用。
            var seededAt = new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
            migrationBuilder.InsertData(
                schema: "vibemaker",
                table: "make_topics",
                columns: new[] { "id", "name", "description", "instructions", "sort_order", "is_enabled", "created_at", "updated_at" },
                values: new object[,]
                {
                    {
                        new Guid("0199b7a0-0000-7000-8000-000000000005"),
                        "建立 RAG 知識庫",
                        "引導整理文件，在專案建立以 SQLite 儲存的向量知識庫，並根據文件問答、顯示出處",
                        "這是「引導」主題：向量資料庫由 Ymir 平台在「專案頁的知識庫」背景建立，每個專案一份 SQLite，原始檔與索引都在平台受保護的儲存空間，不在你的工作目錄。因此不要自己建立 SQLite、不要在工作目錄安裝或執行 embedding／向量套件、不要寫 RAG 程式，這次也不建立任何檔案。請用繁體中文，一次只推進一個步驟，等使用者回覆再繼續。\n" +
                        "步驟一，釐清需求：先問使用者要放進知識庫的文件類型與大約份數、主要語言、想問的問題類型（查規定、找出處、摘要比對等），以及文件是否含機密或個資；已提供的資訊不要重複詢問。\n" +
                        "步驟二，檢查是否適用：支援 TXT、Markdown、文字型 PDF、DOCX；單檔上限 20 MB、每專案 200 份、約 20,000 段。掃描影像的 PDF 無法擷取文字，需先轉成文字型 PDF 或 DOCX；Excel、PPT、圖片目前不支援，建議先另存為 DOCX／PDF／TXT。提醒文件會被切成約 800 字的段落並轉成向量，資料依平台政策處理。\n" +
                        "步驟三，操作引導：(1) 知識庫屬於專案，沒有專案的對話不能使用，請使用者先建立或進入要使用的專案；(2) 在專案頁找到「知識庫」區塊，上傳整理好的文件；(3) 等每份文件狀態變成「已就緒」才算建好，失敗的文件可看原因後重試，同名檔案再上傳視為新版本，新版本成功才會取代舊版本；(4) 若頁面顯示知識庫尚未設定，請使用者聯絡管理員設定 Embedding 模型，不要自行嘗試其他做法。\n" +
                        "步驟四，驗證與使用：請使用者在知識庫區塊提問，確認答案有 [n] 出處；資料不足時系統會直接說明而不是亂答。只有管理員標記可用於知識庫的模型會收到文件片段，其他模型只回傳檢索到的段落。可協助整理文件命名、拆分過大的檔案、設計測試問題清單。\n" +
                        "不可編造平台功能，也不可宣稱已建立好知識庫；是否成功以使用者在專案頁看到的狀態為準。最後用一個簡短檢查清單收尾。",
                        50,
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
                keyValue: new Guid("0199b7a0-0000-7000-8000-000000000005"));
        }
    }
}
