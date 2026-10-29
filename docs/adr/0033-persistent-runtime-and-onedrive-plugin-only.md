# ADR-0033：Agent 執行環境常駐，OneDrive 只保留外掛

- 狀態：已採納（使用者 2026-10-29 要求調整架構並確認選項）
- 日期：2026-10-29
- 補充：[ADR-0011](0011-runtime-lifecycle-policy.md)。閒置停止預設改為 0（常駐）、新增「人為停止」標記、對帳後自動啟動。
- 取代：[ADR-0013](0013-onedrive-connector.md) §3、§4（OneDrive 同步資料夾與工作目錄雙向同步）。ADR-0013 §1、§2（授權、憑證保存）不變。
- 修訂：[ADR-0030](0030-plugin-marketplace-and-onedrive-agent-tools.md) 中「保留 ADR-0013 同步資料夾」的部分。

## 背景

使用者提出兩項架構調整：

1. **執行環境要常駐。** 每個使用者的 Pi agent container 原本閒置 30 分鐘就停止（ADR-0011）。使用者希望 container 一直在線，只在需要時由人停止，這樣才能：
   - 用 Pi agent 跑比較長時間的任務；
   - 在 container 內做排程提醒、管理、自動查找、自動回覆。
2. **OneDrive 只當外掛。** 對話紀錄與工作目錄不再自動備份到 OneDrive，也不再從 OneDrive 同步到執行環境。OneDrive 只在使用者啟用 connector 時，讓 Agent 讀寫自己的 OneDrive（ADR-0030 的 Agent 工具）。

使用者 2026-10-29 確認的選項：

| # | 項目 | 決定 |
|---|---|---|
| 1 | 閒置停止 | 保留設定，預設改為 0（不停止） |
| 2 | 人為停止 | 只有 Admin（沿用「管理 → 總覽 → 停止執行環境」） |
| 3 | 主機或 API 重啟後 | 自動把不是人為停止的 runtime 啟動回來 |
| 4 | OneDrive 同步的程式、設定、資料表 | 全部移除 |

## 決策

### 1. 執行環境預設常駐

- `VibeMaker:Runtime:IdleTimeoutMinutes` 的預設值從 30 改為 **0**，0 表示不自動停止。
- `appsettings.json`、管理介面的說明與摘要（「執行環境常駐」）同步更新。
- 閒置停止的程式與管理介面欄位保留，Admin 設定大於 0 時照舊運作（ADR-0011 §3）。
- 已在管理介面存過執行政策的部署，資料庫的值仍然優先。要常駐，Admin 需把「閒置停止時間」改成 0，或按「還原為部署設定」。

### 2. 人為停止的標記

- `vibemaker.agent_runtimes` 新增 `stop_requested`（bit，預設 false）。以下情況設為 true：
  - Admin 停止執行環境（`admin.runtime.stop`）；
  - 帳號停用或刪除（ADR-0009、ADR-0027）；
  - Admin 開啟的閒置停止（`runtime.idle_stop`）。
- 下一次 execution 經 `EnsureRuntime` 啟動 runtime 時清除。被停止的使用者送訊息時，仍照常自動啟動。

### 3. 對帳後回到應有狀態

`RuntimeLifecycleService.ReconcileAsync` 在 API 啟動時執行。先依實際狀態更新紀錄（ADR-0011 §2），然後：

- **自動啟動**：同時符合以下條件的 runtime 會被啟動，並寫稽核 `runtime.autostart`（actor `system`）：
  - 實際狀態是 `Stopped`，例如主機重開機後 container 停住；
  - `stop_requested = false`；
  - 擁有者是 Active 使用者。
- **網路模式**：與 `ExecutionRunner` 相同，由 `ExtensionPolicyService` 依使用者的對外連線能力決定（ADR-0012 A.8）。受限網路沒有設定時啟動失敗，只寫 log，**不會**退回成可以對外連線。
- **補救停止**：擁有者已停用或刪除、runtime 卻仍在執行時，在這裡停止並設 `stop_requested`。這是停用當下停止失敗時的補救；以前靠閒置停止處理，現在預設不停止。
- **略過的情況**：
  - 使用者正在執行（取不到 `UserExecutionLocks`）時跳過。
  - 群組 runtime（ADR-0026，id 不是使用者）不自動啟動，下一次 `@bot` 時照舊啟動。
  - `NotCreated`（container 已不存在，例如被維護服務回收）不重建，下次送訊息時才建立。
- 自動啟動不算使用者活動，`LastActiveAt` 不變。

### 4. 移除 OneDrive 同步

以下全部移除：

- **後端**：
  - `OneDriveSyncService`、`OneDriveSyncWorker`、`OneDriveSyncOptions`；
  - `ExecutionRunner` 的執行前下載與執行後排入上傳；
  - 對話端點 `GET /api/conversations/{id}/onedrive`、`POST .../onedrive/sync`（含 `ONEDRIVE_NOT_READY`）。
- **根資料夾設定**：
  - `PUT /api/connectors/onedrive/root`（含 `ONEDRIVE_ROOT_INVALID`）；
  - `OneDriveStatusResponse.rootPath`；
  - 稽核 `connector.onedrive.root.update`。
- **資料表與欄位**：migration `RemoveOneDriveSync` drop `vibemaker.onedrive_sync_scopes`、`vibemaker.onedrive_sync_items`，以及 `onedrive_connections.root_item_id` / `root_path`。
- **前端**：檔案面板的「雲端保存狀態」（`app-onedrive-sync`）、外掛卡片的同步資料夾表單。

保留的部分：

- 連結 / callback / 解除連結（ADR-0013 §1、§2）與 refresh token 保存規則。
- 外掛市集與 OneDrive Agent 工具（ADR-0030）：search / list_folder / download（寫到工作目錄 `onedrive/`）/ upload / create_folder / move / delete。

已經同步到 OneDrive 的檔案、以及執行環境內的檔案都保留，不做任何清理。

## 影響

- **資源**：container 常駐會持續占用主機記憶體與 PID，同時在線的 container 數等於用過 Agent 的使用者數。
  - 每人上限（`MemoryLimit`、`CpuLimit`、`PidsLimit`）與主機容量規劃更重要。
  - 資源不足時，Admin 可以停止個別使用者，或把閒置停止改回大於 0。
- **container 內的長任務與排程**：
  - 只在 container 執行期間有效。
  - Admin 停止、帳號停用、主機重開機後，container 內自行啟動的背景程序都會結束。重開機後 container 會自動啟動，但程序不會。
  - Agent 的模型 key 只在每次 execution 以環境變數帶入（ADR-0004），container 內的背景程序拿不到。
  - 平台級的排程與通知仍以 [ADR-0032](0032-proactive-notifications-and-scheduled-tasks.md) 為準。
- **單次執行上限**：`ExecutionTimeoutMinutes` 不變（預設 30 分鐘，最多 240 分鐘），長任務需要時由 Admin 調高。
- **部署**：
  - 套用 `RuntimeStopRequested`、`RemoveOneDriveSync` 兩個 migration。後者 drop 同步資料表，部署前先備份。
  - 部署設定中的 `Ymir__Connectors__OneDrive__MaxFileBytes`、`MaxFiles`、`PollInterval` 等同步設定不再使用，可以移除。`GraphBaseUrl` 仍用於測試替身。
- **未驗證、待使用者環境確認**：以 rootless Podman 實際重開機後自動啟動 container。沙箱以整合測試的 runtime 替身驗證。
