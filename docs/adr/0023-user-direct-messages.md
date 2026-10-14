# ADR-0023：使用者之間的一對一訊息（簡易版 Teams）

- 狀態：已採納
- 日期：2026-10-14

## 背景

使用者要求在 Ymir 內讓同事之間可以直接聯繫，類似簡易版 Microsoft Teams：畫面右側一排使用者名單，點選後即可聊天。
Ymir 目前只有「使用者 ↔ Agent」的對話（Vibe Maker），沒有使用者之間的溝通管道。

## 決策

1. **放在共用核心（Platform），不屬於 Vibe Maker**。訊息是平台層能力，之後的子產品也會用到；資料表 `platform.direct_messages`
   （寄件者、收件者、內文、建立時間、已讀時間），同一模組內對 `platform.users` 建 FK（不串聯刪除：使用者只會停用）。
   第一階段只有一對一訊息：沒有群組、頻道、檔案、表情、編輯或刪除。
2. **授權一律以目前使用者為其中一方**（SA §12）。端點在 `/api/direct-messages`：
   - `GET /contacts`：其他**未停用**使用者的名單，附未讀數與最後往來時間（未讀優先、再依最近往來）；只回 id、顯示名稱、帳號、部門，不回 email
     （與 `/api/users/search` 相同原則，ADR-0016）。
   - `GET /{peerId}`（分頁，`before` 游標）、`POST /{peerId}`（送出）、`POST /{peerId}/read`（標為已讀）。
     路徑上的 id 只是「對方」，查詢條件永遠包含目前使用者，所以拿別人的 id 或訊息 id 都讀不到別人之間的訊息。
   - 收件者不存在、已停用或是自己，一律回 404，不透露帳號狀態。
   - 內文去掉前後空白，1～4000 字；每人每分鐘最多送 60 則（rate limit `direct-message`）。
3. **即時通知用 SSE**（ADR-0002：原生 `EventSource` + cookie）。`GET /api/direct-messages/events` 是每位使用者一條的長連線，
   事件 `ready`（已訂閱）、`message`（新訊息，寄件者與收件者都會收到，讓同一人的其他分頁同步）、`read`（已讀，讀取者與寄件者都會收到）、
   `ping`（25 秒心跳，避免 Cloudflare 切斷閒置連線，ADR-0006；同時重新確認帳號狀態，停用後串流結束，ADR-0009）。
   - 與 execution 事件相同，**先寫資料庫再發佈**。
   - 通知只在 API 程序內傳遞（`InMemoryDirectMessageHub`）：Ymir 目前只部署一個 API 程序（ADR-0008），不引入外部匯流排。
     錯過的通知不補送；前端在每次 `ready` 時重新讀取名單與目前對話，資料以資料庫為準。要水平擴充時改成以資料庫或 Redis 傳遞，介面不變。
4. **前端**：shell 右側新增成員面板（`app-people-panel`），桌面預設展開、可收合並記住（`localStorage`），窄螢幕為右側抽屜；
   上方工具列的成員按鈕顯示未讀總數。訊息只以文字綁定顯示（`white-space: pre-wrap`），不經 innerHTML。
   面板收起或分頁在背景時，新訊息不自動標為已讀。

## 影響

- 訊息內容屬於使用者之間的私人溝通：**不寫入稽核**、不進 log；`DataRetentionWorker` 不清理（與對話相同，永久保留）。
  管理介面目前沒有查看訊息的功能；若有法遵需求另開 ADR。
- 單一 API 程序的假設寫進文件；多程序部署前必須先替換 `IDirectMessageHub`。
- 新端點列入授權矩陣（`NotResourceScoped`），隱私與即時通知在 `DirectMessageTests` 驗證；e2e 腳本 `npm run e2e:dm`。
- 之後可延伸：群組聊天、在線狀態、通知音效 / 桌面通知、附件、把 Agent 產出的檔案或網站分享到訊息。
