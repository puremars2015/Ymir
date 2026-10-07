# 備份與還原指南

> Sprint 5 強化。適用 Linux 正式部署：API 跑在 rootful Podman（Quadlet），runtime host 與 Agent container 跑在 rootless 帳號 `ymir`，見 `deploy/api/README.md`、`deploy/runtime-host/README.md`。
> 路徑以範本的預設值為準；如果改過設定，請換成你的值。

## 要備份什麼

| 項目 | 位置（預設） | 重要性 | 說明 |
|---|---|---|---|
| SQL Server 資料庫 `ymir` | SQL Server（schema `platform`、`vibemaker`） | **必要** | 使用者、本機帳號密碼雜湊、專案、對話、訊息、稽核紀錄、系統設定（含加密的 Entra secret） |
| Data Protection 金鑰 | `/srv/ymir/api/keys/`（容器內 `/var/lib/ymir/keys/`） | **必要** | 遺失後：所有人要重新登入，**資料庫中加密的 Entra client secret 無法解密**，必須在管理介面重新輸入。必須和資料庫**一起**備份與還原 |
| 使用者 workspace | `/srv/ymir/workspaces/users/{userId}/` | **必要** | Agent 產生的檔案（`workspace/`）與 Pi session（`agent-state/`，對話上下文）。container 本身不用備份，遺失時會自動重建（SA §15） |
| 部署設定與 secret | `/etc/ymir/api.env`、`/etc/ymir/runtime-host.env`、`deploy/litellm/.env` | 必要（另外保管） | 含 runtime host token、LiteLLM master key、模型金鑰、DB 連線字串。**不要和一般備份放在一起**，改放公司的 secret 管理工具或離線保管 |
| Cloudflare Tunnel token | `/home/ymir/.config/ymir/cloudflared.env`（600） | 可重新取得 | 可以從 Cloudflare dashboard 重新複製，再到管理介面套用 |
| LiteLLM 資料庫（PostgreSQL） | `deploy/litellm` 的 `litellm-db` volume | 建議 | virtual key、使用者預算與花費紀錄。遺失時 key 會在下一次換發時重建，但**本期花費與用量紀錄會歸零** |
| Agent runtime image | `localhost/ymir/agent-runtime:<版本>` | 不需要 | 可由 `runtime/agent/Containerfile` 重新 build |

**不需要備份**：
- Agent container 本身；
- API container 與 image（由原始碼重建）；
- 已結束 execution 的事件：只用於 SSE 重播，依保存期限自動清理，對話內容在 messages。

## 保存期限（自動清理）

API 每天清理一次，設定在 API 的環境變數（`/etc/ymir/api.env`），0 表示永久保留。

| 設定 | 預設 | 說明 |
|---|---|---|
| `Ymir__Retention__AuditLogDays` | 365 | 稽核紀錄保留天數；請依公司稽核政策調整 |
| `Ymir__Retention__ExecutionEventDays` | 90 | 已結束 execution 的事件保留天數；執行中的不刪，對話訊息不受影響 |
| `Ymir__Retention__IntervalHours` | 24 | 清理間隔 |

- 每次清理會寫一筆稽核 `system.retention.purge`，記錄刪了幾筆。
- 對話、訊息、專案與 workspace 檔案依資料保存原則**永久保留**（刪除只是封存），不會被自動清理。
- 如果公司要求刪除離職人員的資料，目前需要人工處理，屬於後續功能。

## 建議的備份排程

| 項目 | 頻率 | 方式 |
|---|---|---|
| SQL Server | 每天完整備份，必要時每小時交易紀錄備份 | `BACKUP DATABASE`（見下方） |
| Data Protection 金鑰 | 每次變更後（金鑰約 90 天輪替一次）＋每天跟資料庫一起 | 複製目錄 |
| workspace | 每天 | `restic` / `borg` 或公司的檔案備份工具，增量備份 |
| LiteLLM PostgreSQL | 每天 | `pg_dump` |

備份檔請放在**另一台主機或物件儲存**，並加密。workspace 與 Data Protection 金鑰的備份檔權限只給備份帳號。

## 備份步驟

### 1. SQL Server

```bash
# 在 SQL Server 主機（或 container）內執行；路徑是 SQL Server 看得到的位置
sqlcmd -S localhost -U sa -C -Q "BACKUP DATABASE [ymir] TO DISK = N'/var/opt/mssql/backup/ymir-$(date +%F).bak' WITH COMPRESSION, CHECKSUM, INIT"
```

- 使用公司既有的 SQL Server 備份機制時，確認涵蓋 `ymir` 資料庫即可。
- 備份帳號建議用只有 `db_backupoperator` 權限的帳號，不要用 `sa`。

### 2. Data Protection 金鑰

```bash
sudo tar -C /srv/ymir/api -czf /backup/ymir-keys-$(date +%F).tgz keys
sudo chmod 600 /backup/ymir-keys-$(date +%F).tgz
```

### 3. workspace

```bash
# 以 root 或能讀 ymir 帳號檔案的備份帳號執行；rootless Podman 的 user namespace 會讓部分檔案的 uid 看起來不同，備份工具要保留數字 uid/gid
sudo tar --numeric-owner -C /srv/ymir -czf /backup/ymir-workspaces-$(date +%F).tgz workspaces
```

- 資料量大時改用 `restic` / `borg` 做增量備份；
- 備份時不必停止服務。Agent 正在寫的檔案可能只備份到一半，下一次備份會補上。

### 4. LiteLLM

```bash
cd deploy/litellm
docker compose exec -T litellm-db pg_dump -U litellm litellm | gzip > /backup/litellm-$(date +%F).sql.gz
```

## 還原步驟

依序還原，確認每一步的結果後再做下一步。

1. **停止服務**：
   ```bash
   sudo systemctl stop ymir-api          # Quadlet
   sudo systemctl stop ymir-runtime-host
   ```
2. **還原 SQL Server**：
   ```sql
   RESTORE DATABASE [ymir] FROM DISK = N'/var/opt/mssql/backup/ymir-2026-10-07.bak' WITH REPLACE, CHECKSUM;
   ```
   正式環境不會自動 migrate：備份比目前程式舊時，先依 `deploy/api/README.md` 第 4 步執行 `dotnet ef database update`（兩個 DbContext 都要），再啟動 API。
3. **還原 Data Protection 金鑰**：解壓縮到 `/srv/ymir/api/keys/`，擁有者 `1654:1654`、權限 700。**要用和資料庫同一天的備份**。
4. **還原 workspace**：
   ```bash
   sudo tar --numeric-owner -C /srv/ymir -xzf /backup/ymir-workspaces-2026-10-07.tgz
   ```
   - 解壓後確認 `/srv/ymir/workspaces` 的擁有者仍是 `ymir` 帳號；
   - 移除舊的 Agent container（`sudo -u ymir podman rm -f $(sudo -u ymir podman ps -aq --filter name=ymir-user-)`），讓它們以還原後的檔案重建。
5. **還原 LiteLLM**（如果需要）：
   ```bash
   gunzip -c /backup/litellm-2026-10-07.sql.gz | docker compose exec -T litellm-db psql -U litellm litellm
   ```
6. **啟動服務**：先 runtime host，再 API。
   - API 啟動時會自動對帳，把 runtime 狀態更新成實際狀態；
   - 管理總覽的「服務狀態」應該全部正常。

## 還原後的驗證

- [ ] 用本機帳號與企業帳號各登入一次；企業帳號能登入，表示 Data Protection 金鑰與 Entra secret 都正確。
- [ ] 管理 → 系統設定：Entra 顯示「已設定」，沒有「無法解密」的錯誤。
- [ ] 打開一個舊對話：歷史訊息都在，檔案面板看得到 Agent 產生的檔案，可以下載。
- [ ] 在舊對話送一則訊息：Agent 記得上一輪的內容（Pi session 還原成功），檔案仍在。
- [ ] 管理 → 稽核紀錄：看得到還原前的紀錄。
- [ ] 管理 → 用量：連接 LiteLLM 時看得到費用與預算。

## 定期演練

建議每季在測試主機做一次完整還原演練，依上面的清單驗證，並記錄所需時間（RTO）與資料損失範圍（RPO）。
