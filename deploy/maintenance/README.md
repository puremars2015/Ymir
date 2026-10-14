# 主機維護

## Image 回收（`ymir-image-prune`）

Container 以 `--rm` 執行、結束後自動回收，但 image 不會：每次部署 API 都會建立一個新的 `localhost/ymir/api:<commit>`（約 360 MB），建置也會留下沒有 tag 的中間 image。`ymir-image-prune.sh` 依下列規則定期清理：

| Store | 範圍 | dangling（無 tag）image |
|---|---|---|
| `root`（rootful Podman：API、正式 nginx、Tunnel） | `localhost/ymir/*` | 清理 |
| `ymir`（rootless：Agent runtime、LiteLLM proxy） | `localhost/ymir/*` | 清理 |
| `itadmin`（rootless：開發建置、與其他專案共用） | `localhost/ymir/*` | **不清理**（可能是其他專案的建置快取） |

一律保留：

- 任何 container（執行中或已停止）使用中的 image；`podman rmi` 不加 `-f`，仍被使用時會拒絕刪除。
- 正式設定指向的 image：API Quadlet（`/etc/containers/systemd/ymir-api.container`）的 `Image=`、runtime host（`/etc/ymir/runtime-host.env`）的 `VibeMaker__Runtime__Image`。
- 每個 `localhost/ymir/*` repository 最新的 `YMIR_IMAGE_KEEP` 個（預設 3），供回復舊版。回復時 Quadlet 的備份（`/var/backups/ymir/*/ymir-api.container`）指向的 image 如果已被回收，需要重新建置該 commit。

不會碰其他專案的 image、base image（`mcr.microsoft.com/...`、`docker.io/...`）或 volume。

### 使用

```bash
sudo bash deploy/maintenance/ymir-image-prune.sh            # dry run：只列出會刪除的 image
sudo bash deploy/maintenance/ymir-image-prune.sh --apply    # 實際刪除
```

設定（環境變數）：`YMIR_IMAGE_KEEP`（保留數）、`YMIR_IMAGE_STORES`（`名稱:帳號:是否清 dangling`，空白分隔，預設 `root:root:1 ymir:ymir:1 itadmin:itadmin:0`）、`YMIR_API_QUADLET`、`YMIR_RUNTIME_HOST_ENV`。

### 每週自動執行

```bash
sudo install -m 755 deploy/maintenance/ymir-image-prune.sh /usr/local/sbin/ymir-image-prune
sudo install -m 644 deploy/maintenance/ymir-image-prune.service deploy/maintenance/ymir-image-prune.timer /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now ymir-image-prune.timer
systemctl list-timers ymir-image-prune.timer        # 下次執行時間（每週日 04:00 前後）
journalctl -u ymir-image-prune.service              # 每次刪除了哪些 image
```

更新腳本後要重新 `install` 到 `/usr/local/sbin`。

## 主機維護服務（`ymir-maintenance-host`，ADR-0025）

管理介面「管理 → 系統資源」的後端：顯示整台主機的 CPU／記憶體／磁碟、Ymir 的 container 與 image，並依管理員設定的保留天數與資源門檻**安全地**自動回收。只管理 Ymir 的物件，不碰同一台主機上其他專案的 container 或 image。

- 獨立的 root 服務（`src/Ymir.MaintenanceHost`），只聽 Unix socket `/run/ymir-maintenance/maintenance.sock`（660，group `ymir-runtime`）＋獨立的強 token。runtime host 維持 rootless，API 不掛載任何 container runtime socket（ADR-0008）。
- 端點只有：資源查詢、政策同步、回收紀錄、dry run。沒有 shell／exec／任意刪除端點；store、帳號、repository、正式設定檔位置全部只來自 `/etc/ymir/maintenance-host.env`。
- **歸屬**：image 必須同時屬於設定的 repository，且帶 `ymir.managed=true` label（API 與 Agent 的 Containerfile 每個階段都有）或已在 `RegisteredImages` 登記；Agent container 必須帶 `ymir.reclaimable=agent-runtime`（runtime host 新建立的 container 才有）。名稱只是輔助；歸屬不明的 dangling／中間層一律略過。
- **一律保留**：執行中／暫停／建立中的 container、服務 container（API、入口、Tunnel、資料庫）、`ExcludedContainers`、任何 container（含已停止）引用的 image、正式設定指向的 image（API Quadlet `Image=`、runtime host `VibeMaker__Runtime__Image`）、每個 repository 最新 3 個版本、`ProtectedImages`、有其他專案 tag 的 image。設定、引用或狀態讀不到時 fail closed。
- 不刪 volume、workspace、附件、RAG、網站、備份或金鑰；不使用 `--force`，不使用 `podman system prune` / 全 store `image prune`；不會停止任何 container。
- **鎖**：刪除停止的 Agent runtime 前取該使用者的生命週期鎖（runtime host 建立 / 啟動時持有同一把鎖），取不到就略過；刪除 image 期間持有部署鎖。`MaintenanceHost__LockDirectory` 不存在時只提供監控與 dry run。
- 未使用時間從「持續停止」/「持續沒有引用」的首次觀測起算，保存在 `/var/lib/ymir-maintenance/state.json`，重啟不重設。

### 安裝（需要 root）

1. **停用舊的回收 timer**（新舊機制不得同時刪除；`ymir-image-prune.sh` 保留為人工工具，不由網頁呼叫）：
   ```bash
   sudo systemctl disable --now ymir-image-prune.timer 2>/dev/null || true
   ```
2. **鎖目錄**：
   ```bash
   sudo install -m 644 deploy/maintenance/ymir-lifecycle.tmpfiles.conf /etc/tmpfiles.d/ymir-lifecycle.conf
   sudo systemd-tmpfiles --create /etc/tmpfiles.d/ymir-lifecycle.conf
   ```
3. **runtime host 使用鎖**：在 `/etc/ymir/runtime-host.env` 加上 `VibeMaker__Runtime__LifecycleLockDirectory=/run/ymir-lifecycle`，重新 publish runtime host（含這次的程式）並在沒有執行中工作時重啟 `ymir-runtime-host.service`。
4. **publish 與設定**：
   ```bash
   dotnet publish src/Ymir.MaintenanceHost -c Release -r linux-x64 --self-contained -o /tmp/ymir-maintenance-host
   sudo install -d /opt/ymir/maintenance-host && sudo cp -a /tmp/ymir-maintenance-host/. /opt/ymir/maintenance-host/
   sudo install -m 600 deploy/maintenance/maintenance-host.env.example /etc/ymir/maintenance-host.env   # 首次安裝才複製，再填入 token 與 store
   sudo install -m 644 deploy/maintenance/ymir-maintenance-host.service /etc/systemd/system/
   sudo systemctl daemon-reload && sudo systemctl enable --now ymir-maintenance-host.service
   sudo curl -s --unix-socket /run/ymir-maintenance/maintenance.sock http://localhost/health    # ok
   ```
5. **API**：在 `/etc/ymir/api.env` 設定 `Ymir__Maintenance__Endpoint=unix:/run/ymir-maintenance/maintenance.sock` 與相同的 `Ymir__Maintenance__Token`；在 `/etc/containers/systemd/ymir-api.container` 取消註解 `Volume=/run/ymir-maintenance:/run/ymir-maintenance`，並在 `[Unit]` 加上 `After=ymir-maintenance-host.service`；`daemon-reload` 後重啟 API。
6. **盤點登記**：開啟「管理 → 系統資源」。加上 label 之前建置的既有 image 會顯示「無法證明屬於 Ymir」：確認確實是 Ymir 的之後，把 image ID 加入 `MaintenanceHost__RegisteredImages__N` 並重啟服務；要保留的回復版本加入 `ProtectedImages`。
7. **先 dry run**：在頁面按「Dry run（不刪除）」確認候選與保護清單正確後，才勾選「開啟自動回收」。預設關閉。

### 部署時持有部署鎖

建置新 image、改 Quadlet `Image=` 的部署步驟以部署鎖包住，維護服務在這段期間不會刪除任何 image：

```bash
sudo flock /run/ymir-lifecycle/deploy.lock bash <部署腳本>
```

### 限制

- CPU／記憶體壓力只能觸發對安全候選的清理；停止的 container 與 image 通常不占 CPU／記憶體，回收不保證降低負載，也不會停止執行中的工作來達標。沒有安全候選時只留紀錄。
- 磁碟壓力只回收位於受壓檔案系統上的 store，每刪一項就重新量測，達到回復門檻（門檻 + 5%）就停止；紀錄只記檔案系統實測的前後差，不以 image 大小推算。
- Windows Docker / Local 開發環境沒有這個服務，系統資源頁顯示不支援。
