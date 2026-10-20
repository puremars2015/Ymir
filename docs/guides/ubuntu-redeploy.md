# 這台 Ubuntu 的單一重新部署腳本

入口只有一個：[deploy/redeploy-ubuntu.sh](../../deploy/redeploy-ubuntu.sh)。內嵌 Python 標準函式庫，不依賴 `/home/itadmin/ymir-production-install/update-*` 的一次性工具。適用**已安裝的這台 Ubuntu 正式環境**：rootful API、itadmin rootless SQL、ymir rootless Agent 與主機 RuntimeHost。

> 「完整重新部署」是重新建置並切換程式，不是清空／重灌資料庫。腳本不刪除 DB、volume、workspace、keyring、OAuth、舊 image 或備份，不強制中斷執行中的工作，也不安裝缺少的 Gateway／SQL／Tunnel。新主機與尚未安裝的服務仍依[首次安裝指南](ubuntu-install.md)及各服務文件處理。

## 用法

在一般使用者的乾淨 `main` checkout 執行；首次先做唯讀計畫：

```bash
sudo bash deploy/redeploy-ubuntu.sh --dry-run
```

手動更新並重新建置 API／Angular、RuntimeHost 及**已安裝**的可選主機服務：

```bash
sudo bash deploy/redeploy-ubuntu.sh --pull --test --with-services
```

若有 migration，腳本會拒絕未經批准的套用。先確認 SQL 差異是 additive、與舊程式相容且已經隔離測試，再明確加入：

```bash
sudo bash deploy/redeploy-ubuntu.sh --pull --test --with-services --accept-additive-migrations
```

直接在終端輸入 sudo 密碼；看到來源計畫後，還需輸入 `DEPLOY <顯示的12碼SHA>` 確認。請勿把密碼／env／完整 inspect 貼到聊天。正式執行需要互動終端，不提供 `--force`、`--skip-backup` 或無人確認繞過。

- `--pull`：由 repo 擁有者 fetch／rebase `origin/main`；不以 root 改 Git 全域設定，不接受 dirty 或非 main。若同步時腳本本身更新，會停下要求重新執行。rebase 衝突保留待人工處理，不 reset／clean。
- `--ref <commit/tag>`：部署已存在的來源快照，不 checkout 或改寫工作分支；來源必須有該 SHA 的成功 CI。搭配 `--pull` 可先 fetch。
- `--wait-idle 300`：暫停入口後最多等 300 秒；預設 60 秒。逾時／查詢失敗就恢復原服務，不殺工作。
- `--with-agent-image`：以 ymir 身分另建唯一 tag 的候選 Agent image，**不改 runtime env／不替換既有 Agent**。要啟用它仍需獨立審查、排程個別閒置容器換版。
- `--skip-runtime-host`：不更新 RuntimeHost；須自行確認協定與共用相依相容。
- `--skip-migrate`：仍檢查 history／critical schema／執行中工作；有待套 migration 就拒絕，不可用它忽略不相容版本。
- `--dry-run`：只讀狀態，不 fetch／rebase、不建目錄／鎖／image，不 publish、不停止服務。不能搭配 `--pull`。

## 執行流程與保護

1. 讀取正式 API／RuntimeHost／SQL 基線，確認 Production／Remote、非 root、唯讀、SQL 持久掛載及安全部署路徑。RuntimeHost 使用**實際 WorkingDirectory**，不假設固定 `/opt/ymir/runtime-host`。
2. 固定 SHA，確認該 SHA 的最新 GitHub `CI` 已完成且 success。網路失敗／CI 未完成／失敗都停止，不略過。`--dry-run` 不做這個外部 CI gate。
3. 手動確認後，同時取得現有 `/run/lock/ymir-production-v2.lock` 與 `/run/ymir-lifecycle/deploy.lock`；建置階段也持鎖，避免與維護回收互相干擾。
4. 使用 Git archive 的乾淨快照。Git／MSBuild／單元測試以 repo 的一般使用者執行；API 在 rootful Podman 建置，Agent 候選映像在 ymir store 建置。保留各 store 的安全邊界。
5. Publish 至**新的唯一 release**，不在運作中的服務目錄覆蓋 DLL。產物有 SHA-256 manifest，API image 有固定 image ID 與來源 revision。
6. 重新核對建置期間的配置／service 路徑沒有改變，暫停現有 nginx／Tunnel 入口，確認 Queued／Running 為零，停止本次寫入者。
7. **不論有無 migration 都備份**：SQL COPY_ONLY＋CHECKSUM／RESTORE VERIFYONLY，匯出 Docker／Podman container 外再比對 SHA-256；私有備份 config、keyring、workspace、選定服務 release、OAuth volume（若存在）等，並 tar compare。LiteLLM／PostgreSQL 不換版，原 volume 保留，不把這份檔案備份當 PostgreSQL 還原驗證。
8. history 必須是來源 migration 的完整前綴，未知／跳號／較新 DB 一律停止；只在明確批准時套兩模組 idempotent SQL，使用 filtered index 必要 SET options。檢查最終 history 與 critical tables；完整 schema 相容性仍須事前審查，不以幾張表存在代替全模型驗收。
9. 備份原 Quadlet／drop-in，精確切 API image 與選定服務的新 release。保留原有效 env／mounts／capabilities／network／user；不旋轉任何憑證。
10. 驗證 API health／providers／login、精確 image／service 路徑、已運作可選服務及 Agent 的模型／Gateway 網路。只用無憑證 liveliness 探針，不發 prompt、不呼叫付費模型或 Graph。恢復入口，若能從既有 env 判定 hostname 就驗證公開登入頁，否則在結果中明確標示尚未驗證。

目前 `--with-services` 只更新已安裝的 MCP Gateway／SiteHost／MaintenanceHost；不會把未安裝的 Gateway 當成成功安裝。nginx、Tunnel、SQL、PostgreSQL、LiteLLM 不重建。此次 API 換版會短暫中斷服務，**不是零停機部署**。

## 紀錄與回復

- 每次以 UTC 時間、來源 SHA 與隨機碼建立 `/var/lib/ymir-deploy/<run-id>/`，root 私有，包含 `result.json`、manifest、migration、私有診斷及 `backup/`。正常不刪除任何紀錄／舊 image；容量需由管理者另行審查。
- 一般切換／健康檢查失敗會恢復原 Quadlet、原 drop-in 與先前 active 的服務；相容 additive migration 保留。不宣稱 schema 回退，也不自動覆蓋 DB。
- **migration 執行中失敗或最終 critical schema gate 未通過**：維持 API／入口關閉，在結果標記 `manual_recovery_required`，由管理者根據備份／私有診斷決定處理；不能自動啟動可能不相容的舊程式。
- 自動回復本身失敗也標為需人工處理，不輸出成功。不要任意重跑或刪備份；原始 DB 備份／private tar／原 Quadlet／service baseline／override state 都保留。
- 真登入、既有加密資料、真 Pi SSE、效能、完整冷開機及 SQL／PostgreSQL 還原演練仍需人工驗收。

## 這台主機的設定與限制

不需要另外準備設定檔即可使用目前的 `itadmin` SQL store。覆寫值可以放在 **root:0600、非 symlink** 的 `/etc/ymir/redeploy.env`，只解析 KEY=VALUE 資料，不作 shell `source`：

```text
YMIR_SQL_CONTAINER=ymir-sql
YMIR_SQL_PODMAN_USER=itadmin
YMIR_API_URL=http://127.0.0.1:5080
YMIR_STATE_DIR=/var/lib/ymir-deploy
YMIR_HEALTH_TIMEOUT=120
```

新主機若是 rootful SQL，須明確將 `YMIR_SQL_PODMAN_USER=''`；不是將這支腳本當成全新 Ubuntu 安裝器。舊版的 agent tag／保留數等設定不再支援，會拒絕未知 key，避免誤以為執行了清理或換 Agent。

依賴：Python 3.10+、Bash、Git、Podman、systemd、runuser、tar、部署使用者的 .NET 10 SDK（支援 `/home/itadmin/.dotnet/dotnet` 或 `/usr/bin/dotnet`）。Agent image／具名雙網路、SQL container 與正式 env 必須先存在。無法從 DB 讀回工作狀態、無持久 SQL volume、缺基本服務、secret 檔案權限不符或原來源 CI 未通過，都會拒絕切換。

測試：`python3 deploy/test-redeploy-ubuntu.py`；只用暫時檔與 mock，不需要 sudo、容器、正式 DB 或 GitHub 網路。語法／mock 通過不等於正式 root 部署完成；本輪未執行新的正式切換。
