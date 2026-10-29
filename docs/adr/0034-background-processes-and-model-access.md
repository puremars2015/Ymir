# ADR-0034：container 背景工作自動恢復與背景模型存取

- 狀態：已採納（使用者 2026-10-29 確認：常駐程序 + 排程、給背景程序個人 key）
- 日期：2026-10-29
- 補充：[ADR-0033](0033-persistent-runtime-and-onedrive-plugin-only.md)（常駐 runtime）、[ADR-0007](0007-one-runtime-per-user.md)（container 主程序）
- 修訂：[ADR-0004](0004-litellm-virtual-keys.md) 第 3 點（virtual key 只以 `exec --env` 在每次執行時帶入）

## 背景

ADR-0033 之後，每人的 Agent container 常駐，主機重開機後 API 也會把它啟動回來。但還有兩個缺口：

1. **背景程序不會恢復。** container 的主程序是 `sleep infinity`。使用者（或 Agent）在 container 內自己啟動的背景程序，在 Admin 停止、主機重開機後結束，container 回來時也不會恢復。
2. **背景程序叫不到模型。** 模型 key 只在每次 execution 以 `exec --env LITELLM_API_KEY` 帶入（ADR-0004），所以背景做不到自動回覆、自動整理這類工作。

使用者 2026-10-29 決定：

| # | 項目 | 決定 |
|---|---|---|
| 1 | 恢復範圍 | 常駐程序 + 排程 |
| 2 | 背景程序的模型存取 | 給使用者自己的 key |

## 決策

### 1. container 主程序改為 `ymir-runtime-init`

- image（`runtime/agent/Containerfile`）的 CMD 改為 `/opt/ymir/bin/ymir-runtime-init`（bash，以 agent 使用者執行，PID 1 仍是 `--init`）。
- `ContainerCommandBuilder` 不再指定指令，使用 image 的 CMD。舊 image 的 CMD 是 `sleep infinity`，所以 image、API、runtime host 的部署順序不會讓 container 起不來。
- 新 container 帶 label `ymir.runtime-init=1`。沒有這個 label 的既有 container，在持有使用者執行鎖的 `EnsureRuntime`（呼叫端指定 network：execution、對帳自動啟動）會被重建，沿用 network 重建的流程與 `runtime.recreate` 稽核。讀檔等不指定 network 的呼叫不重建，避免中斷執行中的 Agent。
- 啟動程式在 container **每次啟動**時執行，所以重開機後的自動啟動、Admin 停止後下次送訊息，都會恢復：

| 位置 | 行為 |
|---|---|
| `/workspace/.ymir/autostart/<名稱>.sh` | 常駐程序。名稱只接受英數與 `. _ -`。結束後以 5 秒到 5 分鐘的指數退避重啟；跑超過 60 秒就重設退避。每 30 秒掃描一次：新增的檔案啟動，修改過的重新啟動，刪除的連同子程序一起停止。 |
| `/workspace/.ymir/crontab` | 排程。由 supercronic 以非 root 執行，時區 UTC；檔案修改後重新啟動。 |
| `/workspace/.ymir/logs/<名稱>.log` | 輸出。超過 5 MB 時只保留後半段。 |

- **supercronic** 版本鎖定 v0.2.33，在 Containerfile 以 sha256 驗證 amd64 / arm64 兩個 binary。
  - 雜湊是 2026-10-29 從官方 release 下載後計算的，SHA-1 與 supercronic README 公布值一致。
  - 升級時以同樣方式更新。
- **隔離不變**：沒有新的掛載、權限或資源設定。
  - 背景工作和 Agent 在同一個 container，受同一組 `--read-only`、cap-drop、pids / memory / cpu 上限與網路模式（ADR-0012 A.8）限制。
  - Admin 停止 container 就會中止所有背景工作。

### 2. 背景程序的模型 key

- `RuntimeCredentialPurpose.Background`：與 Agent 執行用的 key 分開快取與換發，換發時不會撤銷執行中 Agent 的 key。
  - 模型權限與一般執行相同（ADR-0019 / 0020）。
  - 套用每月預算（ADR-0011），費用記在使用者自己。
  - 有效期沿用 `KeyLifetime`（預設 24 小時，到期前 1 小時換發）。
- `BackgroundCredentialService` 寫入 key：
  - 以 `bash -c` 固定腳本從 **stdin** 讀 key，umask 077 原子寫入 container tmpfs 的 `/tmp/.ymir/model-key`。
  - key 不出現在程序參數或環境變數，不寫 workspace 或 host 磁碟；container 停止時 tmpfs 清空。
- `ymir-env <指令>` 讀這個檔、設定 `LITELLM_API_KEY` 後執行指令，例如 `ymir-env pi -p "..."`。Pi 的 `models.json` 已經引用 `${LITELLM_API_KEY}` 並指向平台的模型入口。
- **寫入時機**：
  - container 剛建立或啟動時立即寫入：`ExecutionRunner` 在 `Transition != None` 時寫，對帳自動啟動後也寫。
  - `BackgroundCredentialWorker` 每 5 分鐘對所有符合條件的 runtime 重寫，處理 key 換發、API 重啟，以及由其他路徑啟動造成的 tmpfs 清空。
  - 正在執行的使用者跳過，由 execution 自己處理。
- **不給 key 的情況**：
  - 使用者不是 Active；
  - runtime 不在執行中，或 `stop_requested`；
  - 群組 runtime（container 是成員共用的，ADR-0026）；
  - Local provider（沒有 container）；
  - 使用者沒有可用的模型。
- **管理員開關**：執行政策新增 `BackgroundModelAccess`（部署設定 `VibeMaker__Runtime__BackgroundModelAccess`，預設 true）。關閉時 worker 以空的 stdin 執行寫入腳本，刪除 key 檔，約 5 分鐘內生效。
- 帳號停用或刪除時，`RuntimeCredentialService.RevokeAsync` 會一併撤銷背景 key（既有行為涵蓋所有用途），container 也會停止。

### 3. 讓 Agent 知道怎麼用

`ExecutionRunner` 的平台 system prompt（`BackgroundWork.PersonalPrompt` / `GroupPrompt`）說明三件事：

- autostart、crontab 與 log 的位置；
- `ymir-env` 的用法，以及不要把 key 寫進檔案或印出來；
- 只在使用者要求長時間、定時或自動執行時才建立背景工作，並說明如何停止。

群組的 prompt 註明背景程序不能呼叫模型。

## 影響

- **ADR-0004 的修訂**：
  - 原本 key 只在 execution 期間存在於 Agent 程序的環境變數；現在 container 執行期間，tmpfs 裡會一直有一把背景 key。
  - Agent 本來就讀得到執行時的 key（ADR-0004 背景），所以 prompt injection 的外洩面沒有改變性質。不同的是 key 在 execution 結束後仍可用，最長到換發或 container 停止。
  - 緩解：
    - 模型權限與每月預算限制；
    - 24 小時有效期；
    - 停用即撤銷；
    - Admin 可以全域關閉，或停止個別使用者的 container。
  - 費用風險：背景腳本失控時會持續消耗使用者的每月預算，上限是預算本身。
- **部署**：
  - 重新 build Agent image，並更新 API 與 runtime host。
  - 既有 container 在使用者下次執行時重建：workspace 與 agent-state 是掛載的，檔案保留；`/tmp`、`/home/agent` 的 tmpfs 內容本來就不保留。
- **限制**：
  - 背景 key 最晚在 container 啟動後約 5 分鐘內寫入（立即寫入失敗時）。在此之前 `ymir-env` 以 78 結束，autostart 程序會依退避自動重試。
  - 主機時區與使用者時區無關，crontab 以 UTC 計算。
  - 平台級排程（由平台建立 execution、通知使用者）仍以 ADR-0032 為準。
- **未驗證、待使用者環境確認**：
  - 完整 image build：沙箱的 `podman build` 在既有的 pip 步驟連不到 PyPI；
  - supercronic 在 container 內的實際執行；
  - rootless Podman 主機重開機後的完整恢復流程。
