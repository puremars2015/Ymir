# ADR-0025：Ymir 資源監控與安全自動回收

- 狀態：已採納（使用者 2026-10-15 要求依此文件實作，含新增的主機權限邊界）
- 日期：2026-10-15

## 背景

使用者要求管理員查看 container、image 與主機 CPU／RAM／磁碟資源，設定未使用保留時間，以及剩餘資源低於門檻時自動回收；已確認只管理 Ymir，不管理同主機的其他專案。

正式 API／入口容器在 rootful Podman，Agent 在 `ymir` rootless Podman，開發建置可能在共用的 `itadmin` store。ADR-0008 的 RuntimeHost 沒有 root store 權限，也不能讓 API 直接掛載 Podman socket。現有 `deploy/maintenance/ymir-image-prune.sh` 是人工／timer 工具，其 store-wide dangling 清理不足以證明 Ymir 歸屬，不能直接作為新功能的自動刪除實作。

## 決策

### 1. 管理範圍與介面

- 新增管理分頁「系統資源」：主機資源、Ymir containers、Ymir images、自動回收政策、回收紀錄。
- 主機資源是整台主機的 CPU 使用率與剩餘比例、RAM 總量／可用量／比例；磁碟依各 Podman store 所在檔案系統顯示總量／可用量／比例，同一檔案系統去重。不能把 API 容器自身的 cgroup 指標當成主機資源。
- Container 顯示所屬 store、識別、運行狀態、健康狀態、CPU／RAM 使用量、停止時間、回收資格與保護原因。
- Image 不會「運行」；顯示所屬 store、tag／ID、大小、容器引用數、未使用起算時間、回收資格與保護原因。標示共用 layer，不能以 image 大小加總冒充實際可釋放空間。
- 查詢有採樣時間與可用／過期／不支援狀態；部分 store 查詢失敗不可顯示為零，更不可視為沒有引用。
- 僅 Admin 可讀写；寫入須 XSRF。政策使用既有 `platform.system_settings` 模式，保存與回收結果寫稽核，不回傳 host 路徑、環境變數、掛載細節、憑證或完整 CLI 輸出。

### 2. 主機權限邊界（新增）

- Linux Podman 第一階段新增獨立、最小權限的主機維護服務，不把 RuntimeHost 改成 root，也不把 Podman socket 掛進 API。
- 服務經受權限保護的 Unix socket 與獨立強 token 接受固定的資源查詢、政策同步及紀錄查詢；沒有 shell／exec／任意刪除端點。store 帳號、設定檔與可管理項目全部由主機部署設定決定，瀏覽器不得傳入路徑、store、image 或 container 刪除目標。
- 維護服務需讀取 root store 並以指定帳號查詢 rootless store；root 權限僅限此獨立服務。Windows Docker／Local 開發環境顯示不支援或使用測試替身，不在本期取得額外系統權限。
- Ymir 歸屬由部署端固定允許清單及新建物件的 Ymir 管理 label 證明。名稱前綴只能作輔助，不能單獨作刪除依據。既有物件須部署盤點登記；共用 store 中歸屬不明的 dangling／中間層一律略過。
- 清除歸屬不明建置層需另行人工授權；不使用 `podman system prune` 或全 store `image prune`。

### 3. 政策與未使用定義

- Container 保留天數、image 保留天數分開設定；自動回收預設關閉，0 天代表不依期限回收，並非立即刪除。
- Container 未使用從持續停止起算；image 從持續沒有任何容器引用起算。維護服務持久化觀測狀態，重啟不重設；首次遇到未知物件以首次觀測起算，不以 image 建立時間當作最後使用時間。有引用後重新未使用則重新起算。
- 可分別設定 CPU 剩餘比例、RAM 可用 GB／比例、磁碟可用 GB／比例門檻，0 代表停用該門檻；任一啟用門檻持續不足時進行緊急安全回收，可略過一般保留天數，但不得略過保護規則。
- 建議採樣每 30 秒、壓力持續 5 分鐘才觸發、每輪冷卻 10 分鐘；提供回復門檻遲滯及每轮刪除數量上限，避免短暫負載與重複清理風暴。實際值可由部署預設並於管理介面顯示。
- CPU／RAM 壓力只能觸發對安全候選項目的清理；停止容器與 image 通常不占 CPU／RAM，回收不保證降低負載。頁面須明示此限制；無安全候選或資源仍不足時顯示告警，不能停掉運行中的工作來達標。
- 磁碟壓力按受壓檔案系統選候選，從最久未使用者開始，分批重測實際可用容量，達回復門檻或無候選即停止；不能靠其他磁碟的刪除量判定已解除壓力。

### 4. 不可突破的回收保護

- 不刪運行中／暫停中／正在建立或啟動的容器，不自動停止任何容器，不改變 ADR-0011 的閒置停止政策。
- 自動刪除的停止容器只限可重建且持有明確回收允許標記的 Ymir Agent runtime；workspace 必須外置並確認仍可存取。不刪 SQL、LiteLLM 資料庫、API／入口／Tunnel 等部署服務容器或備份容器。`ymir-sql-old` 明確排除。
- 不刪 volume、workspace、對話附件、RAG、網站產物、備份或加密金鑰；容器可寫層的未知資料不能視為可丟棄。
- 所有容器（包括停止的非回收候選）引用的 image、正式設定指向的 image、每個 Ymir repository 最近至少 3 個不同版本及明確保護的回復版本一律保留。
- 不刪其他專案物件或共用 image；image 有其他非 Ymir tag 時保留。無法讀取設定、歸屬、引用或工作狀態時 fail closed，略過並記錄原因。
- 不使用強制刪除。每個刪除前重新確認狀態、引用與保護，並與 Agent ensure/start 及部署共用受控鎖；鎖協定尚未具備時只提供監控與 dry run，不啟用自動刪除。
- 新舊維護 timer 不得同時刪除；啟用新機制前停用舊回收 timer，既有人工腳本保持獨立且不由網頁直接呼叫。
- 每輪持久化觸發原因、候選／保護／刪除／失敗數、實際磁碟前後可用量與摘要；沒有候選也留摘要。管理者可查閱，不聲稱 image 尺寸即實際回收量。

## 實作與驗證順序

1. 主機服務受限協定、主機資源與分 store 盤點；Admin 查詢與資源分頁。
2. 持久政策、未使用觀測時間、保護分類與 dry run；部署時盤點允許清單與保護版本。
3. 與 RuntimeHost／部署同步的生命週期鎖、受限回收 worker、紀錄與門檻遲滯。
4. 假 CLI／TimeProvider 測試：跨專案、未知 dangling、引用查詢失敗、設定讀取失敗、重啟、停止後再啟動、競態、共享 layer、不同檔案系統、低 CPU／RAM 無候選、安全候選不足等。
5. Admin 授權矩陣、XSRF、主機 token／協定拒絕任意命令與路徑、OpenAPI／前端 schema、後端 build/tests、前端 lint/tests/build、瀏覽器情境驗證。
6. 正式服務安裝與啟用另行授權；先以 dry run 驗證保護清單，未完成前不開啟正式自動回收。

## 影響

新增獨立主機管理信任邊界與部署服務；此 ADR 僅擴充資源盤點及受限回收能力，ADR-0008 的 RuntimeHost rootless、API 不接 engine socket、不接受任意主機參數等限制維持不變。CPU／RAM 監控是全主機資料，回收權限則僅限 Ymir。保護資料優先於強行達到資源門檻。

## 實作（2026-10-15）

- 專案：`src/Ymir.Maintenance`（協定、訊息、`LifecycleLock`、API 端的 `MaintenanceClient`）、`src/Ymir.MaintenanceHost`（root 服務：盤點、分類、門檻、回收、紀錄）。部署檔在 `deploy/maintenance/`（systemd unit、tmpfiles、env 範本、安裝步驟）。
- 協定：Unix socket `/run/ymir-maintenance/maintenance.sock` + bearer token；端點只有 `GET /v1/resources`、`GET|PUT /v1/policy`、`GET /v1/runs?after=`、`POST /v1/dry-run`（`MaintenanceHostProtocolTests` 固定這份清單）。
- 歸屬標記：image 的 `ymir.managed=true`（API 與 Agent Containerfile 每個階段）、Agent container 的 `ymir.managed=true` 與 `ymir.reclaimable=agent-runtime`（`ContainerCommandBuilder`）；既有物件以 `RegisteredImages` 登記。
- 生命週期鎖：`/run/ymir-lifecycle/user-<id>.lock`（runtime host 的 `VibeMaker:Runtime:LifecycleLockDirectory`，建立 / 啟動 / 停止 / 刪除時持有）與 `deploy.lock`（部署腳本以 `flock` 持有）；advisory flock，維護服務以不等待方式取得，取不到就略過。
- API：`/api/admin/resources`、`/policy`、`/runs`、`/dry-run`（Admin + XSRF）；政策存 `platform.system_settings` 的 `maintenance.reclamation_policy`，`MaintenanceSyncWorker` 每分鐘同步並把有動作的回收紀錄寫入稽核 `maintenance.reclaim`。前端「管理 → 系統資源」。
- 驗證以假的 container CLI 與時間（單元）及真正的 Unix socket 服務（整合）完成；正式主機的安裝與啟用另行授權（實作與驗證順序第 6 步）。

