# 對話附件：上傳檔案、圖片、影片給 Agent

> 狀態：第一階段已實作（2026-10-07）。看板 [Sprint 6 #006](../progress/board-sprint-6.md#006--對話可以附加檔案圖片影片給-agent)。

## 問題

對話畫面只能輸入文字，使用者沒辦法把截圖、設計稿、影片、Excel / PDF 等檔案交給 Agent。

## 設計

| 項目 | 決定 | 理由 |
|---|---|---|
| 檔案放哪裡 | 使用者 runtime 的工作目錄 `uploads/{附件 id 末 8 碼}-{檔名}`（專案的對話放在專案目錄、共用） | 與 Agent 產生的檔案同一套保存方式（ADR-0007）；Agent 直接用工具讀；Remote（runtime host）也適用，API 不碰 host 路徑（ADR-0008） |
| 怎麼寫進 runtime | `IWorkspaceFileWriter`：在 runtime 內 `bash` 經 stdin 寫暫存檔、大小一致才 `mv`；目標目錄 `realpath` 必須在工作目錄內 | 與 `IWorkspaceFileReader` 對稱；中斷不留半個檔案；防止 Agent 把 `uploads` 換成指向外面的 symlink |
| 上傳 API | `POST /api/conversations/{id}/attachments?fileName=`，body 是檔案本身（octet-stream） | 不需要 multipart 解析，可串流；API 先寫暫存檔（`DeleteOnClose`）以取得大小與檔頭 |
| 何時上傳 | 按下送出時才上傳，接著送訊息帶 `attachmentIds` | 新對話（首頁、專案頁直接開聊）在送出前還沒有對話 id；也不會留下沒送出的附件 |
| 中繼資料 | `vibemaker.message_attachments`（對話、使用者、訊息、檔名、路徑、類型、大小），送出時綁定訊息、只能綁一次 | 訊息歷史要顯示附件；送出時驗證附件是自己的、同一個對話、尚未送出（SA §12） |
| 給 Agent 的內容 | 後端把附件清單（路徑、類型、大小）附加在 `AgentExecution.AgentPrompt`；對話紀錄只存使用者輸入的文字 | 與 `/make` 相同模式；影片、文件等模型看不到的格式 Agent 也能用工具處理 |
| 圖片直接給模型 | 模型設定 `VibeMaker__Models__N__SupportsImages=true` 時，PNG / JPEG / GIF / WebP（≤ 10 MB、最多 5 張）以 Pi RPC `prompt.images` 送出；models.json 宣告 `input: ["text","image"]`，Pi 會自動縮圖 | Pi 1.0.0 原生支援；不支援視覺的模型不送圖片，避免模型端錯誤 |
| 類型判斷 | 圖片以檔頭（magic bytes）判斷，其他依副檔名，不信任瀏覽器 Content-Type | 避免把任意內容當圖片送給模型 |
| 顯示 | 使用者訊息上方顯示附件：圖片用 blob URL 的 `<img>` 縮圖，其他是可下載的 chip；附件也出現在檔案面板 | 遵守「不得以 innerHTML / iframe 顯示」規則；下載仍走附件下載端點（octet-stream、nosniff、CSP sandbox） |
| 上限 | 單檔 50 MB、每則訊息 10 個 | Cloudflare Tunnel 單一請求上限 100 MB（ADR-0006） |

前端：📎 按鈕選檔、拖放到輸入框、貼上截圖；只附檔不打字時送「請看看我附加的檔案。」。

## 後續（未做）

- 每人工作目錄容量配額（目前只有單檔上限）；超過時拒絕上傳。
- 影片 / 音訊的自動處理（抽影格、轉文字）：runtime image 需要 ffmpeg / whisper，或做成平台 MCP（ADR-0012）。
- PDF / Office 轉文字：同上，視需要加入 runtime image 的工具。
- 上傳進度條（目前只顯示「第 n / N 個」）。
- 真實視覺模型（例如 MiniMax、GPT 的視覺版本）經 LiteLLM 的實測：**待使用者環境確認**。
