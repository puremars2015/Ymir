# OneDrive Workspace 後續計畫

- 記錄日期：2026-10-06
- 狀態：**已選定方法二；僅記錄計畫，尚未實作**
- 使用者指示：日後透過 Microsoft Graph 串接每位使用者自己的 OneDrive；目前先不要修改程式。

## 目標與架構方向

使用者在 Ymir 連結並授權自己的 Microsoft 帳號，Ymir 透過 Microsoft Graph 讀寫該使用者的 OneDrive。OneDrive 作為專案檔案的雲端來源與保存位置，Agent 仍在每位使用者專屬容器內的本機 workspace 工作。

不把 OneDrive API 當成可直接掛載的檔案系統，也不依賴部署主機上的 OneDrive 同步程式。保留 [ADR-0007](../adr/0007-one-runtime-per-user.md) 的使用者隔離與目錄分組，以及 [ADR-0008](../adr/0008-containerized-api-runtime-host.md) 的主機 runtime host 邊界。

```text
使用者授權 OneDrive
        ↓
Ymir 後端／同步服務 ↔ Microsoft Graph ↔ 該使用者的 OneDrive
        ↕ 下載來源、上傳修改
使用者專屬本機 workspace ↔ 使用者專屬 Agent 容器
```

## 預定使用流程

1. 使用者在設定頁連結 OneDrive；後端綁定 Ymir 使用者與 Microsoft 身分、drive ID，並完成授權。
2. 使用者選擇或建立 Ymir 專用的雲端資料夾，專案／未分組對話分別對應自己的子資料夾。
3. 執行任務前，比對雲端版本與本機狀態，下載必要檔案至該使用者的本機工作目錄。
4. Agent 依目前模式操作本機檔案，不取得 Microsoft Graph 憑證。
5. 任務結束後，由後端排程同步新增或修改的檔案；畫面分別顯示「Agent 任務結果」與「雲端保存狀態」。
6. 同步失敗保留本機檔案與待同步紀錄，可重試；授權失效時要求重新連結，不把尚未上傳的檔案當成已保存。

建議首版在任務開始／結束及使用者手動操作時同步。即時雙向同步、分享資料夾與多人協作另列後續範圍。

## 目錄與同步範圍

| 資料 | 預定位置與處理 |
|---|---|
| `workspace/projects/<projectId>` | 本機工作副本，對應使用者 OneDrive 中的專案資料夾 |
| `workspace/chats/<conversationId>` | 本機工作副本，對應未分組對話的獨立雲端資料夾 |
| 原始碼、文件、使用者指定的產出 | 納入檔案同步，依專案設定決定範圍 |
| `node_modules`、套件快取、暫存檔 | 預設排除；實作時定義忽略規則及大小限制 |
| `agent-state`、Pi sessions、外掛與模型設定 | 繼續存於部署主機，不納入本次 OneDrive 同步 |
| Ymir 對話、專案與執行資料 | 繼續存於 SQL Server |
| Microsoft Graph 憑證 | 僅由後端保管；不進 workspace、Agent、瀏覽器儲存或版控 |

同步映射需記錄使用者、專案／對話、drive ID、雲端資料夾／檔案 ID、本機相對路徑與版本。後端驗證擁有者，不能只相信前端傳來的 drive 或 item ID；同時保留路徑與 symlink 邊界檢查。

## 授權、衝突與失敗處理

- 採使用者委派授權方向，後端處理授權與憑證續期；Ymir 登入不代表已獲得 OneDrive 存取權。Microsoft 帳號類型、租戶限制、權限範圍與管理員同意需求待確認。
- 優先限制在 Ymir 專用／使用者選定的資料夾；精確 Graph 權限需依帳號類型及實際 API 能力驗證，不承諾任意資料夾都能透過同一種最小權限存取。
- 憑證加密保存，使用者可解除連結；解除後停止同步，但本機資料與雲端檔案的保留／清理規則需要另行決定。
- 記錄同步基準版本，寫入前檢查版本是否改變。雲端與本機同時修改時顯示衝突並保留兩邊內容，不直接覆蓋。
- 首版不預設雙向傳播刪除；檔案刪除、重新命名與衝突副本的規則需先確認。
- 同一使用者的同步與 Agent 寫檔需協調，避免同步期間讀到未寫完的內容；取消／失敗任務產出的部分檔案如何上傳待決定。
- 處理網路中斷、Graph 限流、授權撤回、雲端容量不足、大檔案與重試；同步工作需能在服務重啟後恢復，避免重複上傳。

## 後續實作階段與驗收

1. **確認條件**：Microsoft 帳號類型、應用程式註冊、同意政策、雲端目錄及保存規則；補充 ADR 後再開發。
2. **連結與映射**：連結／解除 OneDrive、後端憑證管理、每位使用者及每個專案的目錄映射。
3. **最小同步流程**：下載 → Agent 執行 → 上傳，加入同步狀態、排除規則與手動重試。
4. **可靠性**：版本衝突、限流、重啟恢復、刪除／重新命名政策與稽核紀錄。

驗收至少涵蓋：兩位使用者無法讀寫彼此的 OneDrive；Agent 不取得 Graph 憑證；容器重建後能從雲端恢復工作檔案；雲端修改不被無聲覆蓋；同步失敗與重新授權不遺失本機修改；快取與 agent-state 不被上傳。

目前不變更程式、資料表、部署設定或既有 workspace，也不註冊應用程式、要求帳號授權、移動／同步任何實際資料。需使用者另行指示開始實作。

## 參考

- [Microsoft Graph OneDrive 概覽](https://learn.microsoft.com/en-us/graph/onedrive-concept-overview)：以 Graph API 存取 OneDrive，並提供檔案變更追蹤能力。
- [代表使用者取得 Microsoft Graph 授權](https://learn.microsoft.com/en-us/graph/auth-v2-user)：使用者委派授權與授權碼流程。
- [Microsoft Graph 檔案上傳](https://learn.microsoft.com/en-us/graph/api/driveitem-put-content?view=graph-rest-1.0)：實作前依檔案大小與帳號類型確認上傳 API 與權限。
