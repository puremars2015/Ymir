# ADR-0022：讓 Agent 處理使用者指定的 OneDrive 檔案（後端下載、寫回原位置，含刪除、移動、改名）

- 狀態：手動連結（O3～O5）已被 [ADR-0030](0030-plugin-marketplace-and-onedrive-agent-tools.md) 取代（2026-10-16）：改為外掛市集啟用後由 Agent 自行存取；O3 的程式已移除，資料表保留
- 原狀態：已採納（使用者 2026-10-15 要求實作；依 O3～O5 分階段驗證，未完成階段不得宣稱已啟用）
- 日期：2026-10-14
- 依據：使用者要求「讓使用者可以指定 Agent 去操作使用者自己 OneDrive 上的任何文件」；延伸 [ADR-0013](0013-onedrive-connector.md)（OneDrive connector），沿用 [ADR-0012](0012-agent-extensions-and-platform-mcp.md) 的能力開關。

## 背景

- ADR-0013 已讓使用者連結自己的 OneDrive，並取得委派權限 `Files.ReadWrite`。這個權限本來就涵蓋使用者**整個** OneDrive，所以這次**不需要**在 Entra 新增權限，也不需要管理員重新同意。
- 但 ADR-0013 §3 把同步範圍刻意限縮在「同步資料夾」底下的 `projects/<名稱>-<id8>`、`chats/<標題>-<id8>`。使用者 OneDrive 上其他位置的文件（例如 `/文件/2026 預算.xlsx`），Agent 看不到，只能手動下載後再上傳成對話附件，改完再手動傳回 OneDrive。
- 評估過三種做法：

  | 做法 | 說明 | 結論 |
  |---|---|---|
  | A. 使用者指定 | 使用者在對話中選 OneDrive 檔案 / 資料夾，後端下載到工作目錄，執行後寫回原位置 | **採用** |
  | B. OneDrive 平台 MCP | Agent 經 MCP Gateway 自行搜尋、讀寫 OneDrive | 這次不做：Gateway 目前不帶使用者身分給後端，而 refresh token 只在 API；而且被 prompt injection 時，影響範圍是整個 OneDrive。日後確有需求再另立 ADR |
  | C. 把 Graph token 交給 Agent | Agent 直接呼叫 Graph | **不採用**：違反 ADR-0004、ADR-0013「Agent 不持有憑證」 |

- 使用者 2026-10-14 確認的決定（第 3 項在同日改為允許）：

  | # | 項目 | 決定 |
  |---|---|---|
  | 1 | 做法 | A：使用者指定 |
  | 2 | 寫回 | 改過的檔案寫回原位置，帶 `If-Match`；衝突時兩份都保留 |
  | 3 | 刪除、移動、改名 | **允許**：連結範圍內雙向傳播（決策 6）；ADR-0013 的同步資料夾仍不傳播 |
  | 4 | 範圍 | 只限使用者自己的 OneDrive（不含「與我共用」、SharePoint） |
  | 5 | 能力開關 | 沿用 `onedrive` 能力，不另外新增 |
  | 6 | 大小上限 | 單檔沿用 100 MB；連結的資料夾另設檔案數上限 |

## 決策

### 1. 連結項目（linked item）

- 使用者可以把自己 OneDrive 上的**檔案或資料夾**「連結」到一個工作目錄。工作目錄沿用 ADR-0007 的規則：專案對話連結到專案，未分組對話連結到對話自己。
  - 同一個專案的所有對話都看得到專案的連結項目，與專案檔案的行為一致。
- 每個連結可設定**唯讀**（預設：可修改）。唯讀的連結只下載、不寫回。
- 新增資料表 `vibemaker.onedrive_linked_items`：
  - 欄位：id、user id、scope id（專案或對話 id）、drive id、item id、是否為資料夾、唯讀、雲端顯示路徑（只用來顯示）、本機名稱、建立時間；
  - 跨模組只存 user id（ADR-0001）；
  - 每個工作目錄最多 20 個連結（`Ymir:Connectors:OneDrive:MaxLinksPerScope`）。
- 每個檔案的同步基準（item id、eTag、本機大小 + 修改時間 + SHA-256）沿用 `onedrive_sync_items` 的格式，以連結 id 區分，不與 ADR-0013 的資料夾同步混在一起。
- 取消連結只刪除連結與同步基準：雲端檔案不動，工作目錄裡的本機副本保留。取消連結**不是**刪除，不會傳播到雲端。

### 2. 在工作目錄中的位置

- 連結項目放在工作目錄的 `onedrive/<本機名稱>`：
  - 檔案：`onedrive/<id 前 8 碼>-<檔名>`；
  - 資料夾：`onedrive/<id 前 8 碼>-<資料夾名稱>/…`。
  - 本機名稱依 `OneDrivePaths.FolderName` 的規則清理，由後端產生，不接受前端傳入路徑（CLAUDE.md 安全紅線）。
- 檔案讀寫一律經 `IWorkspaceFileReader` / `IWorkspaceFileWriter` 在 runtime 內進行（realpath 檢查、拒絕 symlink、先寫暫存檔再 rename），Local / Podman / Docker / Remote 都適用。
- ADR-0013 的資料夾同步**排除 `onedrive/`**，避免同一份檔案又被複製到 `<根>/projects/…`。
- 執行時，Ymir 把連結項目的本機路徑、「唯讀 / 可修改」，以及決策 6 的規則（刪除、移動、改名會同步到 OneDrive；單一檔案改名要保留 `<id 前 8 碼>-` 前綴）附加到 `AgentPrompt`（比照對話附件的 `AttachmentRules.AppendToPrompt`）。對話紀錄只保留使用者輸入的文字。

### 3. 選擇與驗證：只限使用者自己的 drive

- 前端以檔案選擇器瀏覽或搜尋使用者的 OneDrive，後端一律以**該使用者自己的 access token** 呼叫 Graph：
  - 瀏覽：`/me/drive/root/children`、`/me/drive/items/{id}/children`；
  - 搜尋：`/me/drive/root/search(q=…)`。
- 新增連結時，前端只傳 item id。這是 ADR-0013 §3「不接受前端傳入的 drive 或 item id」的**有限例外**，條件如下：
  - 後端以 `/me/drive/items/{id}` 重新查詢，item 的 `parentReference.driveId` 必須等於連結紀錄中的 drive id，否則拒絕。「與我共用」和 SharePoint 的項目因此無法連結（決定 4）。
  - 不接受 drive id、路徑或 URL。
  - 不能連結 ADR-0013 的同步資料夾本身或其中的項目（後端比對祖先 item id），避免兩套同步互相覆蓋。
  - 不能連結 OneDrive 的根目錄。
- 瀏覽與搜尋結果只回傳名稱、item id、是否為資料夾、大小、修改時間；不回傳下載 URL 或任何 token。

### 4. 下載：每次執行前

- 在 ADR-0013 §4 的同一個時間點（`ExecutionRunner` 在 EnsureRuntime 之後、啟動 Agent 之前，持有使用者執行鎖）處理連結項目：
  - 雲端有變更、本機沒有變更：下載覆蓋本機；
  - 本機有變更、雲端沒有變更：保留本機版本，等執行後寫回；
  - 兩邊都改過：依決策 7 處理衝突；
  - 雲端的刪除、移動、改名依決策 6 套用到本機。
- 資料夾連結會遞迴下載，排除規則與 ADR-0013 相同（隱藏檔、`node_modules`、OneDrive 不接受的檔名、超過大小上限的檔案）。
  - 單檔上限沿用 `MaxFileBytes`（100 MB）；
  - 每個資料夾連結最多 500 個檔案（`Ymir:Connectors:OneDrive:MaxLinkedFolderFiles`），超過時只下載前 500 個並提示。
- 下載失敗不阻擋 Agent 執行：狀態事件提示「OneDrive 連結檔案未更新，使用本機檔案」，與 ADR-0013 相同。

### 5. 寫回：執行後，背景處理

- 執行結束後（完成、失敗或取消都一樣），把可修改的連結排進持久化同步工作，由 `OneDriveSyncWorker` 以 `UserExecutionLocks.TryAcquire` 取得使用者鎖後處理。服務重啟後會繼續，失敗依 ADR-0013 的退避規則重試 5 次。
- **修改過的檔案**：寫回原本的 item，帶 `If-Match: <基準 eTag>`。
- **連結資料夾內新增的檔案**：上傳到連結資料夾內對應的位置，`conflictBehavior=fail`，不覆蓋雲端同名檔案；本機新增的子資料夾在雲端建立。
- **連結資料夾以外的檔案**一律不寫回；唯讀連結也一律不寫回。
- 刪除、移動、改名依決策 6 寫回。

### 6. 刪除、移動、改名（雙向，只在連結範圍內）

使用者決定允許（決定 3）。這只適用於可修改的連結；ADR-0013 的同步資料夾（`projects/`、`chats/`）仍然不傳播刪除與改名。

**本機 → 雲端**（執行後的寫回）：

- **刪除**：基準內的檔案在本機不見了，而且沒有被判定為移動 / 改名時，在雲端刪除該 item。
  - 以 `DELETE /me/drive/items/{id}` 帶 `If-Match: <基準 eTag>`：雲端在這段期間被改過時不刪，改為重新下載到本機，計為衝突。
  - Graph 的刪除會把項目移到 **OneDrive 資源回收筒**（商務版預設保留 93 天），Ymir **不呼叫永久刪除**（`permanentDelete`），使用者可以自行還原。
  - 資料夾連結內空掉的子資料夾：本機刪除整個子資料夾時，雲端刪除該資料夾（同樣進資源回收筒）；只是裡面的檔案都被刪掉時，資料夾保留。
- **移動 / 改名（資料夾連結內）**：本機檔案系統看不出改名，所以以內容比對：基準中「本機不見的檔案」與「本機新出現的檔案」大小與 SHA-256 完全相同、而且一對一時，視為移動或改名。
  - 以 `PATCH /me/drive/items/{id}`（新的 `name` 與 `parentReference`，帶 `If-Match`）處理，item id 不變，雲端的版本紀錄與分享設定保留。
  - 目的地只能是同一個資料夾連結內的位置；比對不到或不是一對一時，退回「刪除 + 新增」。
  - 檔案從連結範圍移到工作目錄的其他地方（例如 `deliverables/`），對雲端而言等於刪除。
- **改名（單一檔案連結、資料夾連結本身）**：本機名稱是 `<id 前 8 碼>-<名稱>`。Agent 或使用者把它改成 `<相同前綴>-<新名稱>` 時，雲端 item 改名為 `<新名稱>`；前綴不同的不視為改名。
  - 單一檔案連結的本機檔案被刪除時，雲端檔案刪除（進資源回收筒），連結狀態改為「已刪除」，保留在清單中讓使用者知道。
- **大量刪除保護**：同一次寫回要刪除的檔案超過 20 個，或超過該資料夾連結基準檔案數的 30% 時（`Ymir:Connectors:OneDrive:DeleteConfirmThreshold*`）：
  - 這次寫回的刪除先**暫停**，修改與新增照常寫回；
  - 連結狀態改為「待確認刪除」，檔案面板列出數量與前幾個檔名，使用者選「確認刪除」或「還原本機檔案」（從雲端重新下載）；
  - 這是為了防止 Agent 因為文件中的惡意指示（prompt injection）或錯誤指令，一次清空使用者的資料夾。

**雲端 → 本機**（執行前的下載，以 item id 對應，雲端的改名與移動不會讓 item id 改變）：

- 雲端刪除、本機未改：刪除本機副本。
- 雲端刪除、本機有修改：保留本機檔案，並在原位置重新上傳成新檔，計為衝突（不讓本機的修改消失）。
- 雲端在連結資料夾內改名或移動：本機跟著移動到對應的位置。
- 雲端移出連結資料夾或移出使用者的 drive：等同從連結範圍刪除，依前兩點處理。
- 單一檔案連結的雲端檔案被刪除時，連結狀態改為「雲端已刪除」；被改名時，本機改成 `<id 前 8 碼>-<新名稱>`。
- 資料夾連結本身在雲端被刪除時，連結狀態改為「雲端已刪除」，本機副本保留，不重建雲端資料夾。

**唯讀連結**一律不傳播：本機的刪除、移動、改名會在下一次執行前由雲端版本還原。

### 7. 衝突

- 雲端與本機在上次同步後都改過（`If-Match` 回 412，或下載前比對到雙方都有變更）時，沿用 ADR-0013 的規則：
  - 原檔名保留雲端版本；
  - 本機版本另存為 `名稱 (OneDrive 衝突 yyyyMMdd-HHmmss).副檔名`，雲端與本機都有兩份；
  - 衝突數顯示在畫面上。
- 本機改名 / 移動、雲端同時修改內容時：先依雲端修改處理內容衝突，移動 / 改名延到下一次寫回。
- OneDrive 本身的版本紀錄可以還原 Agent 寫回前的版本，刪除的項目可以從資源回收筒還原；使用指南會說明這兩點。

### 8. API

- 成員端點，只作用在目前使用者自己身上：
  - 擁有者由 server 端驗證（SA §12）；
  - 狀態變更加 `RequireAntiforgeryHeader()`；
  - 全部加入 `AuthorizationMatrixTests`。

  | 端點 | 用途 |
  |---|---|
  | `GET /api/connectors/onedrive/browse?itemId=` | 瀏覽（省略 itemId 為根目錄） |
  | `GET /api/connectors/onedrive/search?q=` | 搜尋（最多 50 筆） |
  | `GET /api/conversations/{id}/onedrive/links` | 這個工作目錄的連結與各自的同步狀態 |
  | `POST /api/conversations/{id}/onedrive/links` | 新增連結：`{ itemId, readOnly }` |
  | `PATCH /api/conversations/{id}/onedrive/links/{linkId}` | 切換唯讀 |
  | `DELETE /api/conversations/{id}/onedrive/links/{linkId}` | 取消連結（不刪雲端檔案） |
  | `POST /api/conversations/{id}/onedrive/links/{linkId}/pending-deletes` | 大量刪除保護：`{ action: confirm \| restore }` |

- 未連結、授權失效或沒有能力時：瀏覽、搜尋與新增回 409 `ONEDRIVE_NOT_READY`，沒有能力回 403（與 ADR-0013 相同）。
- 執行中仍可新增或取消連結，下一次執行才生效。
- 回給瀏覽器的錯誤只有摘要（SA §10、§12）。

### 9. 能力、稽核與介面

- **能力**：沿用 `onedrive` 能力（決定 5）。管理員關閉後，下載與寫回都停止；連結與本機副本保留，重新開放後繼續。
- **稽核**：
  - 新增 `connector.onedrive.link`、`connector.onedrive.unlink`，記錄 item id 與唯讀設定，不記錄檔名或路徑內容；
  - 寫回沿用 `connector.onedrive.sync`，記錄上傳、刪除、移動 / 改名的檔案數與衝突數；
  - 大量刪除的確認 / 還原記錄 `connector.onedrive.delete.confirm`、`connector.onedrive.delete.restore`（數量，不含檔名）。
- **前端**：
  - 檔案面板的「雲端保存狀態」旁新增「OneDrive 連結檔案」區塊：列出連結、唯讀切換、取消連結、同步狀態、衝突數，以及「待確認刪除」的確認 / 還原；
  - 輸入框的附件選單新增「從 OneDrive 加入」，開啟檔案選擇器（瀏覽 + 搜尋）；
  - 只用文字綁定，CSP 不變。

## 不在這次範圍

- Agent 自行搜尋或開啟 OneDrive 檔案（做法 B，需另立 ADR，包含 Gateway 身分轉送與 prompt injection 防護）。
- 「與我共用」、SharePoint、Teams 檔案（需要 `Files.ReadWrite.All` 或 `Sites.*` 權限）。
- ADR-0013 同步資料夾的刪除與改名傳播（仍不傳播）。
- 跨連結的移動（從一個連結移到另一個連結會視為刪除 + 新增）、永久刪除。
- 即時同步；delta API 增量比對。
- 直接在 OneDrive 上線上編輯（Agent 一律操作工作目錄中的本機副本）。

## 分階段

實作進度（2026-10-15）：O3a 瀏覽／搜尋與 O3b 連結管理／下載已實作，雲端寫回仍關閉。同步基準採獨立 `onedrive_linked_files` 表；連結的唯讀選項不是 OS 唯讀掛載。為避免多套同步重疊，除下列原限制外，新增連結也拒絕包含既有同步根目錄的祖先資料夾及相互包含的連結。O3b 的衝突副本目前只保存在本機；O4 完成前不能宣稱雲端也有副本。UI 暫置於檔案面板，輸入區附件選單待後續整合。正式部署需另外套 migration／驗證。

| 階段 | 內容 |
|---|---|
| O3 | 連結資料表與 migration、瀏覽 / 搜尋 / 連結 API、以 drive id 驗證 item、執行前下載、`AgentPrompt` 附加路徑、資料夾同步排除 `onedrive/`；FakeGraph 補上 item 查詢、搜尋與 `parentReference` |
| O4 | 執行後寫回：If-Match、資料夾內的新檔、衝突副本；連結狀態、前端檔案選擇器與連結區塊 |
| O5 | 刪除、移動、改名的雙向傳播（決策 6）、大量刪除保護與確認 UI、使用指南、`e2e:onedrive` 擴充 |

## 影響

- **安全**：
  - Agent 仍然拿不到任何 Microsoft 憑證，也不需要連到 Microsoft，受限網路（ADR-0012 A.8）下照常運作。
  - 但連結的檔案一旦進了工作目錄，**開著對外連線（`internet` 能力）的 Agent 就有可能把內容送出去**，這跟使用者上傳附件的風險相同。使用指南要提醒管理員：機密文件多的部門可以關閉 `internet` 能力。
  - Agent 可能因為文件內容（prompt injection）去修改、刪除或搬動使用者沒想動的「可修改」連結檔案；允許刪除後，影響比只寫回修改更大。緩解措施：
    - 唯讀選項；
    - 修改、刪除、移動都帶 `If-Match`；
    - 衝突時保留兩份；
    - 只處理連結範圍內的檔案；
    - 刪除只進資源回收筒、不永久刪除；
    - 大量刪除要使用者確認；
    - OneDrive 的版本紀錄。
- **資料**：新增資料表 `vibemaker.onedrive_linked_items` 與 migration；`onedrive_sync_items` 增加連結 id 欄位，或另建同結構的資料表，實作時擇一並記錄在看板。
- **測試**：
  - 單元測試：本機名稱、drive id 驗證、同步資料夾內的項目被拒、移動 / 改名的內容比對（一對一、多對一退回）、大量刪除門檻；
  - 整合測試：FakeGraph 的下載 / 寫回 / 412 衝突 / 唯讀 / 跨使用者拒絕，以及雙向的刪除、移動、改名；FakeGraph 需支援 DELETE（進資源回收筒）與 PATCH 改名 / 移動；
  - 授權矩陣與 OpenAPI 快照。
- **未驗證**：真實 Entra、Microsoft Graph 與 OneDrive for Business 的實際行為在沙箱連不到，會標註「未驗證、待使用者環境確認」，特別是：
  - search 的結果與延遲；
  - `parentReference.driveId` 在商務版的格式；
  - 大型資料夾的節流；
  - 刪除進資源回收筒與 PATCH 移動時 eTag 的變化。
- ADR-0013 §3「不接受前端傳入的 drive 或 item id」在決策 3 的條件下有限放寬；ADR-0013 §4「首版不同步刪除與改名」只對連結項目放寬（決策 6），同步資料夾不變。ADR-0013 其餘決策不變。
