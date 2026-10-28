# ADR-0032：主動通知與排程任務（讓 Agent 能提醒使用者）

- 狀態：提議中（草稿，「待決定」各項需使用者確認後才改為已採納）
- 日期：2026-10-28
- 依據：使用者詢問「Agent 能不能主動提醒或推送訊息給使用者」，要求先寫 ADR 草稿。延伸 [ADR-0023](0023-user-direct-messages.md)（使用者即時通知 SSE）、[ADR-0030](0030-plugin-marketplace-and-onedrive-agent-tools.md)（經 MCP gateway 的內建工具）、[ADR-0011](0011-runtime-lifecycle-policy.md)（執行政策與配額）、[ADR-0002](0002-bff-cookie-auth-for-sse.md)（BFF + Cookie）。

## 背景

目前 Agent 只能被動回應，沒有主動找使用者的能力：

| 面向 | 現況 |
|---|---|
| 觸發 | execution 只在使用者送出訊息（`ExecutionService.SubmitAsync`）或群組 `@bot`（`SubmitForGroupAsync`）時建立；沒有排程或定時觸發。執行結束後 runtime 依閒置政策停止（ADR-0011）。 |
| 送達 | execution SSE 只在使用者看著該次執行時有用；`IDirectMessageHub` 只在程序內推給線上使用者，錯過不補送（ADR-0023），而且套 `MessagingPolicy`（訪客 403，ADR-0027）。 |
| 離線 | `web/public/sw.js` 只做「可安裝」與靜態資源快取，沒有 Web Push；沒有 Email / Teams。 |
| Agent 工具 | Agent 在 container 內碰不到 API，只能經 MCP gateway 使用平台核准的工具；目前內建服務只有 `onedrive`（ADR-0030）。 |

想要支援的情境：

1. **長任務完成通知**：使用者送出後離開頁面，完成或失敗時收到通知。
2. **排程執行**：例如「每個工作日早上 9 點整理我 OneDrive 裡的新檔案並摘要」。
3. **提醒事項**：使用者對 Agent 說「明天下午三點提醒我交報告」。
4. **離線送達**：使用者沒開 Ymir 時，手機或桌面也能收到。

## 決策

整體拆成三層，彼此獨立、可以分階段上線：

```
觸發                         通知                          送達
─────────────────────        ───────────────────────       ─────────────────────────
execution 結束 ──────┐       platform.notifications        站內：使用者事件 SSE（線上）
排程到時 ─────────────┼──▶   （先寫資料庫，再發佈）  ──▶    瀏覽器：Notification API（分頁在背景）
Agent 工具 notify_me ┘       INotificationService          離線：Web Push（PWA / 桌面瀏覽器）
```

### 1. 通知（共用核心，schema `platform`）

- 新資料表 `platform.notifications`：
  - `id`、`user_id`、`kind`、`title`、`body`、`link`、`source_id`、`created_at`、`read_at`。
  - `kind` 是列舉：`execution.completed`、`execution.failed`、`schedule.failed`、`reminder`、`agent.message`。
- 介面 `INotificationService`（`Ymir.Platform`）：
  - 只提供「通知某一位使用者」，沒有廣播、沒有「通知別人」。
  - VibeMaker 已參考 Platform，可以直接使用；模組之間仍不互相參考。
- **先寫資料庫，再發佈**（沿用 execution 事件的規則）。即時推送遺失時，前端重新連線讀清單即可補齊。
- 內容規則（SA §10、§12）：
  - `title` ≤ 80 字、`body` ≤ 500 字，純文字；前端只以文字綁定顯示，不用 innerHTML，也不把 URL 轉成可點連結。
  - `link` **只由伺服器依 kind 與 id 產生**，只能是站內相對路徑，例如 `/conversations/{id}`。Agent 或排程內容不得指定連結。
  - 不得含 stack trace、host path、token、完整 command output。失敗通知只寫錯誤摘要。
- API：
  - `GET /api/notifications`（分頁）、`GET /api/notifications/unread-count`。
  - `POST /api/notifications/{id}/read`、`POST /api/notifications/read-all`（加 `RequireAntiforgeryHeader()`）。
  - 一律以目前使用者為擁有者；別人的通知回 404；全部加入授權矩陣。
- 保留期限：`DataRetentionWorker` 新增 `Ymir:Retention:NotificationDays`（預設 90 天）。通知不寫稽核，寫入通知的那個動作本身各自稽核。
- 帳號停用或刪除時刪除未讀通知與推送訂閱（決策 5），檢查一律用 `Status == Active`。

### 2. 線上送達：使用者事件串流

- 目前每個分頁已經有 `/api/direct-messages/events`，執行中還會再開一條 execution SSE。再加一條通知 SSE，在 HTTP/1.1（本機開發）下多開幾個分頁就會碰到瀏覽器每個網域 6 條連線的上限。
- **建議**：把既有串流泛化成 `/api/events`（使用者事件串流），新增 `notification` 事件：
  - 只套「登入」policy；訪客也收得到通知。
  - 私訊與群組事件仍只送給 `MessagingPolicy` 允許的角色，由伺服器依角色過濾。
  - 舊的 `/api/direct-messages/events` 保留一個版本作為相容別名，前端改用新路徑後移除。
  - `IDirectMessageHub` 改名或擴充為 `IUserEventHub`，仍是程序內實作（單一 API 程序的限制不變，ADR-0023）。
- 事件內容在 `web/src/app/core/` 手寫型別，與後端同步（同 `group-events.ts` 的做法）。
- 前端：
  - 頂列加通知鈴鐺與未讀數。
  - 分頁在背景（`document.visibilityState === 'hidden'`）且使用者授權時，以瀏覽器 Notification API 顯示。
  - 使用者正看著同一個對話時，不顯示該對話的完成通知。

### 3. 觸發一：execution 結束通知

- `ExecutionRunner` 寫完終止事件後建立通知（best effort，失敗只寫 log，不影響 execution 狀態）。
- **只在需要時通知**：
  - 執行時間超過門檻（預設 60 秒，部署設定 `VibeMaker:Notifications:LongExecutionSeconds`）。
  - 或由排程觸發（決策 4）。
  - 使用者取消的不通知。
- 群組 `@bot`：只通知呼叫者，不通知其他成員。群組成員本來就會收到 Bot 回覆的群組訊息（ADR-0026）。

### 4. 觸發二：排程任務

- 新資料表 `vibemaker.scheduled_tasks`：
  - `id`、`user_id`、`conversation_id`、`kind`、`prompt`、`model_id`、`thinking_level`。
  - `schedule_type`（`once` / `cron`）、`cron_expression`、`time_zone`（IANA，由瀏覽器提供，伺服器驗證）、`next_run_at`（UTC）。
  - `status`（`PendingConfirmation` / `Active` / `Paused` / `Ended`）、`created_by`（`user` / `agent`）、`created_at`、`last_run_at`、`last_result`、`consecutive_failures`。
  - `rowversion`（並行控制）。
- **兩種任務**：

  | kind | 到時做什麼 | 成本 |
  |---|---|---|
  | `reminder` | 只建立一則 `reminder` 通知，不啟動 Agent | 不呼叫模型 |
  | `agent_run` | 在綁定的對話建立 execution，結束後通知 | 計入呼叫者的配額與預算 |

- **執行方式**（`ScheduledTaskWorker`，API 程序內的背景服務）：
  - 每 30 秒找 `next_run_at <= now` 且 `Active` 的任務；以 rowversion 更新 `next_run_at` 取得這一輪的執行權，避免重複觸發。
  - `agent_run` 一律走與使用者送出訊息**相同的檢查**：
    - 帳號有效、模型權限（ADR-0019 / 0020；`auto` 照 ADR-0031 路由）、擴充能力與外掛開關。
    - 每日次數、排隊上限、月預算（ADR-0011）。
    - 這些只在觸發當下判斷，不以建立排程時的結果為準。
  - execution 的 `ClientRequestId` 以 `(task id, 預定觸發時間)` 產生決定性的 UUID（v5），沿用既有冪等鍵；worker 重啟或重複取得同一輪時不會建立兩次。
  - 對話訊息以使用者訊息保存排程內容，加註來源為排程（新欄位 `messages.source`，例如 `schedule`），畫面顯示「排程執行」標記。
  - 同一對話已有執行中的 execution（filtered unique index 回 Conflict）時，這一輪記為「略過：對話忙碌中」，不排隊、不補跑。
  - 失敗時建立 `schedule.failed` 通知。連續失敗 3 次自動改為 `Paused` 並通知使用者。
  - API 停機期間錯過的觸發不逐輪補跑：重啟後若錯過，最多補跑一次，然後由 cron 計算下一次。
- **限制**（部署預設值，管理員可在「系統設定 → 執行政策」調整，值存 `vibemaker.runtime_policy`）：
  - 每人有效排程上限（預設 10）。
  - cron 最短間隔（預設 1 小時）。
  - 對話封存或帳號停用、刪除時，該對話或該使用者的排程改為 `Ended`。
- **群組**：第一階段不支援群組排程。群組 runtime 擁有者與費用歸屬不同（ADR-0026），另議。
- API：
  - `GET /api/scheduled-tasks`、`POST /api/scheduled-tasks`。
  - `PUT /api/scheduled-tasks/{id}`、`POST /api/scheduled-tasks/{id}/pause|resume|confirm`、`DELETE /api/scheduled-tasks/{id}`。
  - 只能管自己的；建立時驗證 `conversation_id` 屬於自己且未封存。
  - 寫稽核 `schedule.create|update|pause|resume|delete|confirm`，target 是 task id，不記 prompt 內容。
- 前端：
  - 對話輸入框旁的「排程」入口（綁定目前對話）。
  - 個人設定新增「排程與提醒」頁，列出所有排程、下次執行時間、最近結果。

### 5. 離線送達：Web Push

- 部署設定 `Ymir:Notifications:WebPush`：
  - `PublicKey` / `PrivateKey`（VAPID）、`Subject`。
  - 私鑰只放部署 secret（`deploy/api/.env`），不得進版控、不進 Agent container、不出現在任何回應或 log。
  - 沒有設定時停用，前端不顯示「推送到這台裝置」。
- 新資料表 `platform.push_subscriptions`：
  - `id`、`user_id`、`endpoint`、`p256dh`、`auth`、`user_agent`（截斷）、`created_at`、`last_success_at`、`failure_count`。
  - API：`POST /api/push-subscriptions`、`DELETE /api/push-subscriptions/{id}`；`GET /api/push/public-key` 給前端訂閱用。
- **防 SSRF**：`endpoint` 必須是 https，主機必須在允許清單內：
  - `fcm.googleapis.com`、`updates.push.services.mozilla.com`、`*.notify.windows.com`、`web.push.apple.com`。
  - 清單只來自部署設定。
  - 伺服器送出時不跟隨 redirect，並有逾時限制。
- **內容最小化**：推送內容雖然有端對端加密（RFC 8291），仍會經過 Google / Apple / Microsoft 的推送服務。
  - 預設 payload 只放 kind、通知 id 與泛用標題（例如「Ymir：任務已完成」）。
  - 使用者點開後，前端以 cookie 讀通知內容。
  - `IncludeBody=true` 才放完整標題與內文，由部署決定。
- 推送服務回 404 / 410 時刪除該訂閱；登出時前端取消這台裝置的訂閱並通知伺服器刪除。
- `sw.js` 新增 `push` 與 `notificationclick`：點擊只開啟同源的站內相對路徑，其他一律開首頁。
- iOS 需要 16.4 以上，而且要「加入主畫面」後以 PWA 開啟才收得到（現有 `pwa-rules.ts` 已有安裝提示）。
- API 需要能對外連到上述推送服務（API container 是 host network，部署文件要註明防火牆規則）。

### 6. 觸發三：Agent 的通知工具（內建 MCP 服務 `notify`）

- 照 ADR-0030 的 OneDrive 模式：
  - gateway 的內建服務 `notify`（`servers.json` 不得使用），轉送到 API 的 `/internal/mcp/notify`。
  - 不在 OpenAPI，帶 `Cf-Connecting-Ip` 一律 404。
  - 後端憑證由簽章金鑰衍生；token 帶 execution id。
  - 每次呼叫重新檢查 execution 執行中、屬於該使用者、帳號有效。
- 工具：

  | 工具 | 行為 | 限制 |
  |---|---|---|
  | `notify_me` | 立即建立一則 `agent.message` 通知給**這次 execution 的使用者** | 每次 execution 最多 3 則；每人每小時 20 則 |
  | `schedule_reminder` | 建立 `reminder` 排程（`once`，或 `cron` 的提醒），只發通知、不啟動 Agent | 不得早於 1 分鐘後；計入每人排程上限 |
  | `list_reminders` / `cancel_reminder` | 只列出、只取消 Agent 在這個對話建立的提醒 | — |

- **Agent 不能直接建立 `agent_run` 排程**：
  - 會花錢、會以使用者的權限反覆執行，被 prompt injection 時風險高。
  - Agent 只能提出建議：以 `propose_schedule` 建立 `PendingConfirmation` 的排程，並發出通知。
  - 使用者在 UI 按「確認」後才改為 `Active`（`created_by = agent` 記錄來源）。
- 通知對象固定是 execution 的使用者，**沒有任何參數可以指定其他使用者**。群組執行時 `notify_me` 只通知呼叫者，`schedule_reminder` 不提供。
- 能力管制：
  - 擴充政策新增 `notify` 能力（全域預設 + 每人覆寫，ADR-0012）。建議預設允許：只通知自己，風險低。
  - 外掛市集列出「通知與提醒」，使用者可以自己關閉（`user_plugin_settings` key `notify`，預設開啟）。
- 寫稽核：`notify.agent.message`、`notify.agent.reminder`（target 是通知或排程 id，不記內容）。gateway 照舊稽核 `mcp.tool.call`。
- 沒有設定平台 MCP（`Ymir:Mcp:GatewayUrl`）時這組工具不可用，決策 1～5 不受影響。

### 7. 不在本 ADR 範圍

- **Email**：經 Graph `Mail.Send` 需要應用程式權限或共用信箱，牽涉 Entra 設定。
- **Teams**：需要 Bot 註冊或 incoming webhook。
- Email / Teams 另立 ADR。決策 1 的通知表與 `INotificationService` 設計成可以加新的送達管道，不需要改觸發端。
- 群組排程、群組通知設定、通知偏好（每種 kind 的開關）留到第一版上線後依回饋決定。

## 分階段

| 階段 | 內容 | 前置 |
|---|---|---|
| N1 | 通知表、`INotificationService`、使用者事件串流、鈴鐺 UI、瀏覽器 Notification、長任務完成 / 失敗通知 | — |
| N2 | 排程任務：`reminder` 與 `agent_run`、管理頁、執行政策上限 | N1 |
| N3 | 內建 MCP 服務 `notify`：`notify_me`、`schedule_reminder`、`propose_schedule`、`notify` 能力與外掛開關 | N2、MCP gateway |
| N4 | Web Push：VAPID、訂閱、`sw.js` push 處理、端點允許清單 | N1 |

N1 單獨上線就能解決「離開頁面後不知道任務做完了」；N4 可以與 N2、N3 並行。

## 影響

- **新 migration**：Platform（`notifications`、`push_subscriptions`）、VibeMaker（`scheduled_tasks`、`messages.source`）。
- **API 契約**：新端點加入授權矩陣與 OpenAPI 快照；使用者事件串流的事件型別前後端同步手寫。
- **單一 API 程序**：
  - 事件 hub 與 `ScheduledTaskWorker` 都假設只有一個 API 程序，與現況一致（ADR-0023）。
  - 排程以 rowversion 與決定性冪等鍵避免重複觸發，多副本時仍正確；但即時推送要改成跨程序（資料庫或 Redis）才能水平擴充。
- **成本**：`agent_run` 排程會在使用者不在場時花費模型費用。所以要有每人上限、最短間隔、連續失敗暫停；預算檢查沿用 LiteLLM（ADR-0004）。
- **部署**：
  - Web Push 需要 VAPID 金鑰（部署 secret）與 API 對推送服務的出站連線。
  - N3 需要更新 MCP gateway 以支援內建服務 `notify`。
- **runtime host 協定不變**：排程只是另一個建立 execution 的入口。

## 待決定（請使用者確認）

| # | 項目 | 建議 |
|---|---|---|
| 1 | 第一個要上線的階段 | N1（長任務完成通知）先上，風險最低、立即有感 |
| 2 | 使用者事件串流 | 泛化成 `/api/events`，取代 `/api/direct-messages/events`（保留一版別名），不另開第二條 SSE |
| 3 | Agent 能不能直接建立會啟動 Agent 的排程 | **不能**：只能 `propose_schedule`，使用者確認後才生效 |
| 4 | Web Push 內容 | 預設只放泛用標題，內容要點開後才讀取（`IncludeBody=false`） |
| 5 | 預設上限 | 每人 10 個有效排程、cron 最短 1 小時、長任務門檻 60 秒、`notify_me` 每次執行 3 則 |
| 6 | `notify` 能力預設 | 全域預設允許、使用者可在外掛市集關閉 |
| 7 | Email / Teams | 這次不做，另立 ADR |
