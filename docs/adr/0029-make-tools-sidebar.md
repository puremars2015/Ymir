# ADR-0029：左側「小工具」：`/make` 小工具自動發布並在 Ymir 內以 iframe 開啟

- 狀態：已採納（使用者 2026-10-15 要求實作）
- 日期：2026-10-15
- 修訂：ADR-0016（小工具網站預設只有擁有者能看；SiteHost 的 `frame-ancestors` 與網站 cookie 屬性）、`SecurityHeaders`（Ymir CSP 加上網站網域的 `frame-src`）

## 背景

使用者要求：左側選單新增「小工具」區塊，放 `/make 小工具架設` 產生的頁面連結。使用者說明：

- 產生的主要是**前端網頁**，直接出現在左側，點開後用 **iframe 嵌入**使用者自己做的頁面；之後平台才會提供統一的後端服務讓小工具註冊使用。
- 一開始只顯示**自己的**小工具；之後再讓開發者指定分享給特定的人。

限制：Agent 產生的 HTML 不得在 Ymir 網域上執行（CLAUDE.md：不得以 innerHTML / iframe 在 Ymir 網域顯示 Agent 產生的內容），
`srcdoc` / blob iframe 會繼承 Ymir 的 CSP（不允許 inline script），也不能用。網站託管（ADR-0016）已經把使用者的網頁放在獨立的網站網域。

## 決策

### 1. 哪些對話是小工具

- `make_topics.produces_tool`：Admin 在「管理 → Make 主題」勾選「產生小工具」；預設的「小工具架設」主題由 migration `MakeTools` 設為 true。
- 產生小工具的主題，`MakePromptBuilder` 一律附加平台規定（不交給 Admin 編輯）：成果放在工作目錄的 `tool/`，主頁面 `tool/index.html`，只做前端、不依賴後端或需要登入的服務。
- 送出 `/make` 時：點的主題 `ProducesTool`，或 `/make <描述>` 而啟用中的主題有產生小工具的（Agent 可能選中它），就把對話標成 `conversations.makes_tool`。

### 2. 自動發布

- `ExecutionRunner`：個人對話（非群組）執行**成功**、對話 `MakesTool`、網站託管已設定時，在寫入完成事件**之前**呼叫 `SiteService.PublishToolAsync`，
  讓前端收到完成事件時就能重新讀取清單。best effort：失敗只送出摘要狀態事件並寫 server log，不改變執行結果。
- `tool/index.html` 不存在（Agent 還在問需求）時什麼都不做，不留下失敗紀錄。存在時：第一次建立網站（`sites.is_tool = 1`，來源 `tool`，名稱 = 對話標題），
  之後重新發布同一個網站（版本切換、保留數量沿用 ADR-0016）。一個對話最多一個小工具網站（filtered unique index）。
- 小工具網站建立時是 `SelectedUsers` 且沒有分享對象：**只有擁有者（與 Ymir Admin）能看**。之後要分享給特定的人，沿用網站的存取設定（ADR-0016 §3）；
  左側清單目前只列自己的。小工具網站也出現在「我的網站」，可以取消發布或刪除，並計入每人網站上限。

### 3. 左側清單與開啟

- `GET /api/tools`：自己的、已發布的小工具（id、名稱、來源對話、更新時間）與網站託管是否已設定。
- 前端 `ToolStore`：登入後讀取、每次執行結束重新讀取；網站託管已設定（或已有小工具）才顯示「小工具」區塊。
- `/tools/:siteId`：以 `POST /api/sites/{id}/ticket` 取得一次性票據的網站網址（ADR-0016 §4），檢查是網站網域的 http(s) 絕對網址（不是 Ymir 自己的來源）後放進 iframe；
  iframe 加 `sandbox`（允許腳本、表單、彈出視窗與同源——這裡的「同源」是網站自己的網域）。「開新分頁」另取一張票據。

### 4. 安全標頭與 cookie

- Ymir 的 CSP 加上 `frame-src {scheme}://*.{網站網域}[:port]`（只在設定 `Ymir:Sites:BaseUrl` 時）；Ymir 本身仍然 `frame-ancestors 'none'`。
- SiteHost 所有回應加 `Content-Security-Policy: frame-ancestors 'self' {PlatformUrl 的來源}`：網站只能被自己或 Ymir 前端嵌入，其他網站不能把私人網站框起來誘騙點擊。
  因此 `SiteHost:PlatformUrl` 必須是使用者實際開啟 Ymir 的網址。公開網站也不能再被其他網站嵌入。
- 網站 cookie：網站網域與 Ymir 不同 site 時，Lax cookie 不會隨 iframe 送出；所以 https（與瀏覽器視為安全來源的 `*.localhost`）改為
  `SameSite=None; Secure; Partitioned`（依最上層網站分開保存，其他網站嵌入時拿不到），仍是 HttpOnly、只限該 hostname、綁定網站。SiteHost 只提供 GET 靜態檔案。

## 影響

- VibeMaker migration `MakeTools`：`make_topics.produces_tool`、`conversations.makes_tool`、`sites.is_tool` 與 filtered unique index。
- 正式環境要使用小工具，需要先部署 SiteHost 與網站網域（萬用字元 DNS / TLS / Tunnel，`deploy/sites/README.md`）；未設定時左側不顯示小工具區塊、也不會自動發布。
- 後續：小工具分享給特定的人並列在對方左側；平台後端服務（註冊、資料儲存）另立 ADR。
