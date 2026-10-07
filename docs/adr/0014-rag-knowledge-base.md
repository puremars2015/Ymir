# ADR-0014：RAG 知識庫——專案文件索引、經 LiteLLM 的 Embedding、每專案一份 SQLite

- 狀態：已採用（2026-10-08）
- 相關：[RAG 知識庫計畫](../planning/rag-knowledge-base-plan.md)、ADR-0001（模組化單體）、ADR-0004（LiteLLM virtual key）、ADR-0007（一人一個 runtime）、ADR-0008（API 容器化、runtime host）

## 背景

使用者希望把自己的文件交給 Ymir，然後根據這些文件問答，並且看得到答案的出處。計畫文件已經確定了幾件事：

- 每個專案各自一份向量資料庫；
- Embedding 是平台共用的服務；
- 預設使用 SQLite，之後可以換成外部向量服務。

尚未決定的有：

- Embedding 模型與硬體；
- 文件格式與容量；
- 外部回答模型的資料政策；
- 服務與 volume 的邊界。

使用者 2026-10-08 的決定：

1. **Embedding 經 LiteLLM 呼叫**（`/v1/embeddings`）。模型由部署設定指定，可以是本機的 TEI 容器，也可以是雲端模型，Ymir 程式只認 LiteLLM。測試使用 Fake Embedding。
2. **回答模型由管理員標記**：只有標記為可用於知識庫的模型才能生成回答。沒有可用模型時，知識庫只能檢索、不生成回答。

## 決策

### 1. 範圍與權限

- 知識庫屬於**專案**。沒有專案的對話不能使用知識庫，也不會預設跨專案合併資料。
- 首版**只有專案擁有者**可以匯入、管理與問答。分享另行設計。
- 所有端點在伺服器端驗證擁有者（SA §12），並加入授權矩陣。
- 專案封存後，知識庫一併不可用（端點回 404）。

### 2. 儲存與邊界

- 原始文件與每專案一份的 SQLite 都放在 **API 自己的持久 volume**：
  - `Ymir:Knowledge:Root/<userId>/<projectId>/`，底下有 `documents/<documentId>/<version>` 與 `index.sqlite`；
  - 路徑只由 id 推導，不接受外部路徑。
- 這個 volume **不是** Agent 的 workspace，不掛給 Agent container，也不經 runtime host。ADR-0008 禁止 API 掛載的是使用者 workspace 與 container runtime socket，平台自己的資料 volume 不在此限，做法與 Data Protection 金鑰目錄相同。
- 管理資料放在 SQL Server（schema `vibemaker`）：文件、版本、狀態、段落數、錯誤摘要。向量與段落文字放在該專案的 SQLite。
- SQLite 由 API 程序內的單一寫入者處理：每個專案一把鎖、WAL 模式、busy timeout。不放在網路檔案系統或 OneDrive 同步目錄。
- 備份：停止寫入或使用 SQLite backup API 取一致性快照（`docs/guides/backup-restore.md` 補充）。

### 3. Embedding

- 介面是 Application 層的 `IEmbeddingClient`，Infrastructure 層經 LiteLLM `/v1/embeddings` 實作。
- 使用**該使用者的 virtual key**（ADR-0004），用量與預算歸使用者；Ymir 不另存金鑰。
- 模型名稱設定在 `VibeMaker:Rag:EmbeddingModel`。沒有設定時，知識庫功能停用，介面顯示「尚未設定」。
- 每個索引版本都記錄模型名稱與向量維度。查詢時，模型或維度不一致的索引不會被使用，介面提示需要重建索引，不混用新舊向量。
- 呼叫時分批（每批 32 段），並設定逾時。失敗時整個版本標記為失敗，舊版本繼續有效。

### 4. 向量儲存

- 介面是 `IVectorStore`：寫入一個版本的段落、刪除版本、搜尋、健康狀態。RAG 流程不依賴 SQL 語法。
- 首版的 provider 是 SQLite（`Microsoft.Data.Sqlite`）：
  - 向量以 float32 blob 存放；
  - 搜尋時讀出該專案有效版本的向量，做**餘弦相似度暴力搜尋**。
- Spike 結果（2026-10-08，沙箱 CPU）：2 萬段 × 1024 維，寫入約 1.2 秒，搜尋約 250 毫秒（純量迴圈，未使用 SIMD）。首版上限設為**每專案 20,000 段**，足夠使用。
- `sqlite-vec` 目前沒有官方 NuGet 套件（只有第三方封裝），而且需要隨平台安裝 native library。首版不採用；之後需要時，在同一介面下新增 provider。
- 外部向量服務是之後的階段（計畫第 4 階段），本 ADR 不實作。

### 5. 擷取與切段

- 支援格式：
  - **TXT、Markdown**：以 UTF-8 解碼；
  - **文字型 PDF**：用 `PdfPig`（Apache-2.0）逐頁取文字，保留頁碼；
  - **DOCX**：以 `System.IO.Compression` 讀 `word/document.xml` 的段落文字，不執行巨集、不讀外部關聯。
- 掃描 PDF（沒有可擷取的文字）、空白文件、其他格式：狀態為失敗，錯誤摘要是「無可擷取文字」或「不支援的格式」，不產生假的成功。
- 切段：
  - 依段落與句號邊界合併，每段約 800 字元、重疊 100 字元；
  - 每段記錄文件、版本、序號、頁碼（PDF）；
  - 切段策略版本記錄在索引中。
- 文件內容視為不可信資料：只當作檢索文字，不執行，也不改變任何權限；log 不記全文。

### 6. 限制（可設定，`VibeMaker:Rag:*`）

| 項目 | 預設 |
|---|---|
| 單檔大小 | 20 MB |
| 每專案文件數 | 200 份 |
| 每專案段落數 | 20,000 段 |
| 單次檢索段數 | 6 段 |

### 7. 索引工作

- 狀態：`Pending` → `Indexing` → `Ready` / `Failed`。資料表有持久化的工作欄位，由背景 `KnowledgeIndexWorker` 處理，做法比照 OneDrive 同步的 polling + signal。
- 同一個專案同一時間只處理一份文件。
- 新版本完整寫入後才切為有效，之後才刪除舊版本的段落。失敗時保留舊的有效版本。
- 服務重啟後，卡在 `Indexing` 的工作改回 `Pending` 重做：先刪除該版本已寫入的段落，不會產生重複段落。
- 同專案同檔名只有一個有效文件（filtered unique index）；上傳同名檔案視為新版本。
- 移除文件：先刪除段落，再把文件標記為已移除。之後的查詢不會再引用；原始檔一併刪除。

### 8. 問答

- 流程：
  1. 問題向量化；
  2. 只在該專案、模型相容的有效版本中，取前 k 段；
  3. 最高分低於門檻（預設 0.2）或沒有任何段落時，回「資料不足」，不呼叫模型。
- 回答模型必須是管理員標記 `AllowKnowledgeBase` 的模型（`VibeMaker:Models:N:AllowKnowledgeBase=true`）。沒有時回 403 摘要；介面只顯示檢索到的段落，不生成回答。
- 經 LiteLLM chat completions、使用者的 virtual key 生成回答：
  - system prompt 要求只依片段回答、以 `[n]` 標註來源、文件沒有涵蓋時直接說明；
  - 片段放在明確標示的資料區塊，避免文件內的指令被當成指示。
- 回應內容：回答、引用（文件名稱、版本、段落序號、頁碼、摘錄）、是否資料不足。不含伺服器路徑。
- 首版以 JSON 一次回傳，不串流。問答不寫入對話紀錄，也不經 Agent，所以與 Agent 執行環境無關。

## 結果

- 新增：
  - Application：`IEmbeddingClient`、`IVectorStore`、`KnowledgeBaseService`；
  - Infrastructure：LiteLLM embedding 與 chat 呼叫、SQLite store、擷取器、背景 worker；
  - 資料表 `vibemaker.knowledge_documents`；
  - 端點 `/api/projects/{projectId}/knowledge/*`；
  - Fake LLM 的 `/v1/embeddings`。
- 部署：API 容器多一個持久 volume（`Ymir:Knowledge:Root`），納入備份。
- 未驗證、待使用者環境確認：
  - 真實 Embedding 模型的繁體中文檢索品質與速度；
  - LiteLLM 的 embedding 路由設定；
  - 正式資料量下的搜尋延遲。
