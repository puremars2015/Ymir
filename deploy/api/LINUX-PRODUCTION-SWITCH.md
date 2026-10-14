# 目前 Ubuntu 主機切換至正式部署

目標：Cloudflare → `ymir-web:80` nginx → `127.0.0.1:5080` Production API（含 Angular build）→ Unix socket RuntimeHost → `ymir` 帳號 rootless Podman Agent。API、nginx、Tunnel 使用 rootful Podman Quadlet；保留 nginx 入口，但不再使用 4200 或 `dotnet run`。

nginx 由 `ymir-origin-production.service` 管理，容器名稱為 `ymir-web`。Tunnel 與 nginx 都使用 host network；Tunnel 的 `AddHost=ymir-web:127.0.0.1` 將名稱導向僅監聽 loopback 的 nginx 80 埠，不直接連到 Kestrel。舊 `itadmin` rootless `ymir-web` 與新 rootful 容器是不同儲存區，舊容器保留停止狀態，不刪除。

## 執行

此腳本是 **目前主機的修復工具，不是全新安裝程式**。沿用 `/etc/containers/systemd` 的 API／Tunnel 映像、`/etc/ymir` 密鑰、既有資料容器與 RuntimeHost。不更新程式、不套 migration、不重建 SQL／PostgreSQL、不刪除舊入口容器。

1. 在 Cloudflare Dashboard 確認該 Tunnel 的兩個 Public Hostname（`ymir.thetainformation.com`、`vibemaker.webpromaterial.com`）來源維持 `http://ymir-web:80`；若已改為直接連 5080，請改回。切換期間網站會中斷。API 接受的 hostname 與 Entra redirect URI 仍需和對外網域一致。
2. 在專案根目錄執行：
   ```bash
   sudo bash deploy/api/activate-linux-production.sh
   ```
3. 確認來源後輸入 `SWITCH`。腳本先備份設定，再檢查 LiteLLM／RuntimeHost、停用使用者層開發 API、安裝正式 nginx 設定、解除正式 API／nginx／Tunnel 遮罩並啟動。nginx 保留 Host 與 HTTPS forwarding，關閉 SSE buffering、允許 50 MiB request body，阻擋 health 與 dev 登入。
4. 若 LiteLLM `4000` 仍無法連線，腳本停止切換；需先修復既有 Podman 埠轉送。不要用重建資料容器來處理。

## 驗證與回復

腳本檢查 API `/health`、`/login` 與 Production 禁止 `/api/dev/login`；另檢查 nginx 登入頁、阻擋 health／dev 登入，並確認 Tunnel service active。尚需人工驗證公開首頁、`/api/me` 回 401、公開 `/health` 回 404、HSTS、企業登入、對話及 SSE。未呼叫真實付費模型。

錯誤時腳本嘗試恢復原 Quadlet、原 systemd 遮罩及原 API 啟動狀態；已啟動的依賴服務保留。Cloudflare Dashboard 改動不在自動回復範圍，必要時手動將來源改回原本 `http://ymir-web:80`（舊入口原先已停止，恢復設定不表示網站可用）。

設定備份位於 `/var/backups/ymir/production-*`，**內含密鑰，不要貼出或提交版本庫**。這不是資料庫備份。`ymir-sql`（itadmin 的 rootless Podman）自 2026-10-14 起將 `/var/opt/mssql` 掛在 named volume `ymir-sql-data`，容器重建不會遺失資料；建立時加 `--stop-timeout 60`，讓 SQL Server 有時間正常關閉（預設 10 秒會被 SIGKILL）。搬移前的容器保留為已停止的 `ymir-sql-old`，確認無誤後可刪除。volume 只防止容器重建遺失資料，不是備份：仍需安排 SQL 原生備份（見 [備份與還原](../../docs/guides/backup-restore.md)）與還原驗證。

已驗證 Bash／內嵌 Python 語法、說明入口、拒絕非 root 執行、Quadlet 轉換重複執行一致性，並以隔離的 nginx:alpine 容器通過 `nginx -t`（非 root、唯讀、NET_BIND_SERVICE）。腳本會另外以既有正式 nginx 映像執行隔離設定檢查。實際 sudo 部署、Cloudflare 與登入驗證待主機執行。
