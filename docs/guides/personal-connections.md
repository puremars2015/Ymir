# 個人連接與工具

## 入口與權限

到「個人設定 → 管理連接與工具」（`/settings/connections`）。頁面分成 Connector 帳號連接、個人 MCP、管理員開放的擴充能力。

- **Connector**：目前支援 OneDrive；沿用 Microsoft OAuth 與帳號隔離。不提供任意 Connector 上傳或網址註冊。
- **個人 MCP**：管理員必須同時開放 MCP 及對外連線，才可新增、啟用或測試。權限關閉後仍可查看摘要、停用及移除。
- **平台 MCP**：仍由開發人員維護服務目錄、管理員設定存取清單；使用者不能改寫平台服務或憑證。

## OneDrive

在卡片連結、重新授權或解除帳號，設定既有同步資料夾，或瀏覽自己的 drive。原 `/settings?onedrive=結果` 回跳網址會導向新入口，不需重新註冊 Microsoft callback URI。

帳號授權不會把整個 drive 自動交給 Agent。選定檔案／資料夾請到對話檔案面板；任意連結項目目前只下載、不寫回原位置。原同步資料夾的上傳／下載規則仍保留，詳見 [OneDrive 指南](onedrive.md)。

## 新增個人 MCP

1. 填寫小寫英數／連字號名稱（以字母開頭，最多 40 字）。每人最多 20 個，不能重複名稱。
2. 填寫 HTTPS streamable HTTP MCP endpoint，例如 `https://tools.example.com/mcp`。只接受 443，不接受 URL 帳密、查詢參數或 fragment；不支援 stdio 命令、任意 headers、OAuth MCP 或套件上傳。
3. 如需要驗證，填寫自己的最小權限 Bearer token。token 為選填、最長 4096 字，以加密密文保存；API 不回傳明文，畫面只顯示是否已設定。提交後輸入框立即清空。
4. 按「新增並啟用 MCP」。從下次**個人**執行開始可用；個人註冊與憑證不會帶入群組共用環境。
5. 可按「測試連線」驗證 MCP initialize；不會呼叫任何工具。測試只允許公開 IP、禁用 proxy／cookies／redirect，限制十秒及 64 KiB 回應，每位使用者每分鐘最多六次。私網或 localhost 服務會被拒絕，失敗回應僅顯示摘要。

**憑證邊界**：個人 MCP token 在執行時會透過環境變數送入你的 Agent 程序；設定檔只寫環境變數引用，不寫 token。但同一個人 runtime 中的程式可能讀到它，因此不要使用管理員／平台帳號憑證。這與只留在 API 的 OneDrive refresh token 不同。

停用／移除會影響下次執行，不會中止已在執行的程序或撤銷遠端服務的 token。懷疑外洩請直接到服務端撤銷憑證。移除只刪除 Ymir 註冊與儲存的憑證，不刪遠端服務、資料或已取得的本機檔案。此首版不支援修改網址／替換 token；請移除後重新新增。

## 部署與驗證

部署前備份資料庫與 Data Protection keyring，套用 VibeMaker migration `20261015093415_PersonalMcpServers`，再更新 API／前端。部署完整 main 時也須檢查並套用兩模組其他尚未套用的 migration（例如 Platform 的 UserAdminManagement），不能只套本功能的 migration。不需要新增 host mount 或 RuntimeHost endpoint。本功能不代表已完成正式部署或真實第三方 MCP 驗收。

本機測試指令（隔離 SQL、Development API 提供 Angular build）：

```bash
cd web
CHROMIUM_PATH=<Chromium 路徑> npm run e2e:connections -- <截圖目錄> http://127.0.0.1:<測試 API port>
```

腳本限制 localhost／127.0.0.1，建立測試帳號及註冊，驗證個人設定導覽、權限、憑證不回顯、測試失敗摘要、啟停、撤銷後移除、舊 OAuth 回跳與窄螢幕。請勿指向正式資料庫。
