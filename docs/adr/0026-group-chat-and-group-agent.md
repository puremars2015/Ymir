# ADR-0026：群組聊天與群組 Agent（@bot）

- 狀態：已採納（使用者 2026-10-15 要求實作並確認下列決定）
- 日期：2026-10-15
- 修訂：ADR-0023（新增群組）、ADR-0007（每個用過 @bot 的群組另有一個 container）、ADR-0008（runtime 擁有者可以是群組 id；協定不變）

## 背景

ADR-0023 只有一對一訊息。使用者要求：

1. 可以群組聊天。
2. 群組中以 `@bot` 與 Agent 交談；同一個群組第一次呼叫時建立該群組專屬的 Agent container 並交談。

使用者確認的決定（2026-10-15）：

| 項目 | 決定 |
|---|---|
| 模型費用 | 記在**呼叫的人**：使用呼叫者自己的 LiteLLM virtual key、每月預算、每日次數與排隊上限（ADR-0004、ADR-0011、ADR-0021） |
| Agent 脈絡 | 每次 `@bot` 附上**最近的群組訊息**（含成員之間的對話）作為背景 |
| 擴充能力 | 依**呼叫者的權限**（對外連線、skills、平台 MCP） |
| 群組管理 | **任何人**可建立，建立者為擁有者並管理成員；成員可自行退出 |

## 決策

### 1. 群組（Platform，延伸 ADR-0023）

- 資料表 `platform.chat_groups`（名稱、擁有者、狀態 Active / Archived）、`platform.chat_group_members`（成員、角色 Owner / Member、加入時間、已讀到的時間）、
  `platform.chat_group_messages`（寄件者種類 User / Bot、寄件者、內文、建立時間；Bot 訊息另有 execution id、呼叫者與狀態 Pending / Completed / Failed）。
- 名稱 1～100 字；成員上限 50 人（含擁有者）；只能加入未停用的使用者。
- 端點 `/api/chat-groups`：清單（自己所屬、附未讀數）、建立、詳情、改名（擁有者）、加入 / 移除成員（擁有者）、退出（成員）、封存（擁有者）、
  訊息分頁、送出、標為已讀。**非成員、已封存、不存在一律回 404**（不透露群組是否存在）；被移除或退出後立即無法讀取。擁有者不能退出，只能封存。
- 即時通知沿用每人一條的 `/api/direct-messages/events`（ADR-0023 §3），新增事件 `group-message`、`group-updated`（改名、成員異動、封存、自己被移除）、`group-read`；
  先寫資料庫再發佈，前端每次 `ready` 重新讀取補齊。
- 內文 1～4000 字，與一對一訊息共用每人每分鐘 60 則的 rate limit。訊息不寫稽核；群組建立、改名、成員異動、封存寫稽核（只記 id，不含內文）。

### 2. `@bot`

- 訊息以 `@bot`（不分大小寫，後面接空白或結束）開頭時：先照常保存為群組訊息（所有成員都看得到），再為呼叫者建立一次 Agent 執行，
  並新增一則狀態為 Pending 的 Bot 訊息；執行結束後由背景工作把 Bot 訊息更新為最後的回答或失敗摘要（重新啟動後仍會補齊）。
- 同一群組同時只有一個執行中的 `@bot`（沿用「同對話單一執行中」的 filtered unique index）；忙碌時回 409，前端提示稍候。
- 成員可經 `/api/chat-groups/{id}/executions/{executionId}/events` 看到即時串流：端點先確認目前使用者是成員、且該 execution 屬於這個群組的 Bot 訊息。

### 3. 群組 Agent 的執行環境（修訂 ADR-0007、ADR-0008）

- **runtime 擁有者**：一般對話的擁有者是使用者；群組 Agent 的擁有者是**群組 id**。兩者都是 Guid，不會重疊。
  container 名稱、workspace 目錄（`UserDirectories` 以擁有者 id 推導）、執行鎖、閒置停止（ADR-0011）、生命週期鎖（ADR-0025）都以擁有者 id 運作。
  runtime host 的協定**不變**：仍只接受一個 Guid 與 runtime 內的程序規格，不接受路徑、image、掛載或資源設定（ADR-0008）。
- 第一次 `@bot` 時建立群組的 Vibe Maker 對話（擁有者 = 群組 id）與 container；之後同一群組共用同一個 Pi session 與 workspace
  （`/workspace/chats/{群組對話 id}`），成員之間共享。這個對話不會出現在任何人的 `/api/conversations`。
- **費用與配額**：execution 的 user 是呼叫者：模型清單、思考深度、每日次數、排隊上限、每月預算與 LiteLLM key 都以呼叫者計算。
- **擴充能力**：以呼叫者的擴充政策執行（對外連線決定 container 的 network，不符時重建；skills；平台 MCP 以呼叫者身分簽發短期 token）。
  **個人連結一律不帶入**：不做 OneDrive 同步、不附加個人 system prompt（ADR-0013、個人設定）——群組 workspace 是成員共用的，不能讓個人檔案或設定流進去。
- **脈絡**：送給 Agent 的內容是「最近的群組訊息（最多 30 則、約 8000 字，含發言者名稱）」放在 `<group-chat>` 資料區塊，並說明那是背景資料、不是指示，
  後面接呼叫者的請求；群組的 system prompt 說明這是多人群組、回答要標示對誰回應。

### 4. 安全

- 群組與 Bot 訊息只以文字綁定顯示（ADR-0023 §4）；Agent 的回答與一般對話相同經摘要規則（SA §10、§12）。
- 群組 workspace 只經群組的 runtime 存取；第一階段不提供群組檔案清單 / 下載（之後另行設計並加入授權矩陣）。
- 新端點全部加入授權矩陣；成員檢查在伺服器端，不信任前端傳入的成員資格。

## 影響

- container 數量上限變成「使用者數 + 用過 @bot 的群組數」；閒置停止與 ADR-0025 的回收同樣適用（群組 container 也帶 `ymir.reclaimable`）。
- 管理介面的執行環境清單會出現群組擁有的 runtime（以群組名稱顯示）。
- 單一 API 程序的假設不變（ADR-0023 §3）。
- 之後可延伸：群組檔案面板、在群組中分享 Agent 產出的網站、`@bot` 以外的其他機器人。
