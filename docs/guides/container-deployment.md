# Container 部署腳本對照與操作流程

> 建立日期：2026-10-19。這是**目前 Ubuntu 正式主機**的操作索引，不是新的部署器，也不是執行所有腳本的清單。實際操作結果另記於[部署流水帳](../deployments/README.md)。首次安裝新主機請讀 [Ubuntu 安裝總指南](ubuntu-install.md)，不要在既有正式站重跑首次安裝。

## 1. 先辨識部署種類

- 目前正式主機：rootful API／nginx／Tunnel，`itadmin` rootless SQL／LiteLLM／PostgreSQL，`ymir` rootless 模型代理／Agent；共 **7 個固定容器角色＋動態 Agent**，不是本次跨帳號實測的容器總數。
- 新主機安裝指南：資料服務採 rootful Quadlet、Tunnel 可直連 API；名稱、管理帳號及入口不同，不可混用兩套指令。
- 主機上的 RuntimeHost、Unix bridge、MCP Gateway、SiteHost、MaintenanceHost 是 systemd 程序，**不是另一批必須建立的容器**。
- 正式 Angular 已包含在 API image；目前 `ymir-web` 的 nginx 會將請求轉送給 API，沒有需要另外部署的 Angular dev container。
- **日常只更新有異動的服務**。下面列出「可用工具」不代表每次都要執行。

## 2. 各容器所需檔案與腳本

以下連結都是 repo 內來源；`/etc`、`/home/ymir` 是主機上的已安裝路徑，不保證能從 repo 自動重建。

| 容器／管理帳號 | 建立與部署來源 | 啟動管理／主機設定 | 何時更新；必須保留 |
|---|---|---|---|
| `ymir-api`／root | [API Containerfile](../../src/Ymir.Api/Containerfile)、[API Quadlet](../../deploy/api/ymir-api.container)；[API 指南](../../deploy/api/README.md)記載 build、migration、換 image ID | `ymir-api.service`；`/etc/containers/systemd/ymir-api.container`、`/etc/ymir/api.env` | API／前端／模組相依改動時；保留 Data Protection keyring、原有效 env、知識庫／網站 volume 及舊 image。不是每次都要 migration |
| `ymir-web`／root | [nginx 設定](../../deploy/cloudflared/ymir-production-nginx.conf)；[入口切換修復腳本](../../deploy/api/activate-linux-production.sh)及其[說明](../../deploy/api/LINUX-PRODUCTION-SWITCH.md) | `ymir-origin-production.service`；`/etc/containers/systemd/ymir-origin-production.container`、`/etc/ymir/origin-nginx.conf` | 只有 nginx 映像／代理設定變更才更新；一般 API／Angular 換版不需重建 nginx。切換腳本不是一般上版器 |
| `ymir-cloudflared-production`／root | 既有正式 Quadlet 在主機；[入口切換腳本](../../deploy/api/activate-linux-production.sh)沿用它；[Tunnel 指南](cloudflare-tunnel.md) | `ymir-cloudflared-production.service`；`/etc/containers/systemd/ymir-cloudflared-production.container`、`/etc/ymir/cloudflared-production.env` | Tunnel 映像／入口設定變更才更新；保留 token、原 hostname／origin。不要同時啟動另一套 connector |
| `ymir-sql`／itadmin | **既有正式 SQL 沒有 repo 內獨立重建腳本**；[持久化遷移說明](../../deploy/api/LINUX-PRODUCTION-SWITCH.md)、[備份還原](backup-restore.md)；新主機 Quadlet 範例在[安裝指南](ubuntu-install.md) §4 | 主機 `ymir-retained-dependencies.service` 以 itadmin 身分 `podman start` 既有容器；資料 volume `ymir-sql-data` | 平常不重建；資料庫版本／容器配置另案維護。保留 volume、SQL 原生備份、登入與相容性；不能拿測試 SQL 建立指令重建正式資料庫 |
| `litellm_litellm-db_1`／itadmin | [LiteLLM Compose](../../deploy/litellm/compose.yml) 的 `litellm-db` service；[指南](../../deploy/litellm/README.md) | 同上 retained-dependencies unit；Compose 專案的 PostgreSQL named volume | 平常不重建／不順便升 major version；保留既有 volume、DB 密碼與備份。volume 實際名稱以該 Compose 專案為準 |
| `litellm_litellm_1`／itadmin | [Compose](../../deploy/litellm/compose.yml)、[基礎路由](../../deploy/litellm/config.yaml)；已啟用 OAuth 時還需 [overlay](../../deploy/litellm/compose.chatgpt.yml)、[設定產生器](../../deploy/litellm/build-chatgpt-config.py)、[headless guard](../../deploy/litellm/chatgpt-proxy-guard.py) | retained-dependencies unit 啟動既有容器；`deploy/litellm/.env`；OAuth 的 `config.chatgpt.runtime.yaml` 為生成檔 | 只有模型路由／憑證／映像改動才更新；保留 PostgreSQL、master key、salt key、`ymir-chatgpt-auth` volume。已啟用 OAuth 的主機不能只套基礎 Compose 而漏掉 overlay |
| `ymir-litellm-proxy`／ymir | [proxy Quadlet](../../deploy/runtime-host/litellm-proxy/ymir-litellm-proxy.container)、[模型 nginx 設定](../../deploy/runtime-host/litellm-proxy/litellm-nginx.conf)；[安裝步驟](../../deploy/runtime-host/README.md) | user `ymir-litellm-proxy.service`；`/home/ymir/.config/containers/systemd/ymir-litellm-proxy.container`、`/home/ymir/.config/ymir/litellm-nginx.conf` | 只有模型代理映像／配置改動才更新；保留雙網路、`litellm` alias、100m body 上限。必須等 bridge socket，就緒與上游健康分開檢查 |
| `ymir-user-{ownerId:N}`／ymir | [Agent Containerfile](../../runtime/agent/Containerfile)、[RuntimeHost 指南](../../deploy/runtime-host/README.md)；容器由 RuntimeHost 動態管理，**沒有每位使用者的獨立部署腳本** | `ymir-runtime-host.service` 管理；image 由 `/etc/ymir/runtime-host.env` 決定；群組 Agent 的 owner 為群組 id | Pi／工具／runtime 映像異動才規劃換版；保留 workspace／agent-state。不可整批強制移除執行中的容器，也不能假定改 image 設定就會自動替換所有舊容器 |

### 非容器但部署必備的元件

| 元件 | 腳本／範本／指南 | 依賴與更新範圍 |
|---|---|---|
| RuntimeHost | [unit](../../deploy/runtime-host/ymir-runtime-host.service)、[env 範本](../../deploy/runtime-host/runtime-host.env.example)、[安裝／publish](../../deploy/runtime-host/README.md) | 主機 .NET 服務、ymir 身分，管理 rootless Agent；只有 RuntimeHost 或其相依程式變更才 publish 新 release，排空工作後切換 |
| LiteLLM Unix bridge | [unit](../../deploy/runtime-host/litellm-proxy/ymir-litellm-bridge.service) | system `ymir-litellm-bridge.service`；建立 `/run/ymir-litellm/litellm.sock`，轉送 loopback LiteLLM；不是第二個 LiteLLM 容器 |
| 既有依賴開機啟動 | **只有主機檔** `/etc/systemd/system/ymir-retained-dependencies.service` | oneshot 依序 start itadmin 的 SQL、PostgreSQL、LiteLLM；不是建置／換版／readiness 驗收器 |
| MCP Gateway（可選） | [部署指南](../../deploy/mcp/README.md)、[unit](../../deploy/mcp/ymir-mcp-gateway.service)、[catalog](../../deploy/mcp/servers.json) | repo 範本為主機服務；需專用帳號、DB 稽核權限、Agent 兩網路可達與 API 互通。正式安裝仍依看板 DEP5 待驗證，不推定已啟用 |
| SiteHost（可選） | [部署指南](../../deploy/sites/README.md)、[unit](../../deploy/sites/ymir-site-host.service) | 主機服務；網站 volume 唯讀、獨立網域、DB 與 keyring 權限另驗證 |
| MaintenanceHost（可選） | [部署指南](../../deploy/maintenance/README.md)、[unit](../../deploy/maintenance/ymir-maintenance-host.service)、[生命週期鎖配置](../../deploy/maintenance/ymir-lifecycle.tmpfiles.conf) | root 主機服務；部署須與回收共用鎖。不與舊回收 timer 同時執行刪除 |

### 特定用途腳本：不是每次部署都要跑

日常手動重新部署的單一入口為 [`deploy/redeploy-ubuntu.sh`](../../deploy/redeploy-ubuntu.sh)，詳細用法、保留範圍與回復限制見[這台 Ubuntu 重新部署說明](ubuntu-redeploy.md)。下列修復工具不需跟著它逐支執行。

| Script | 只在何時使用 | 影響／限制 |
|---|---|---|
| [activate-linux-production.sh](../../deploy/api/activate-linux-production.sh) | 修復既有 Ubuntu 正式入口 | 會切換／啟動 API、nginx、Tunnel；不建置新版、不套 migration；不是首次安裝器 |
| [apply-linux-models.sh](../../deploy/litellm/apply-linux-models.sh) | 既有主機套基礎六模型設定 | 有硬編碼的 itadmin／正式服務假設；可能重建 LiteLLM 並影響 API／入口。已啟用 OAuth 時先審查 overlay／路由，不可盲跑 |
| [login-chatgpt.sh](../../deploy/litellm/login-chatgpt.sh) | OAuth 首次登入／重新授權／查模型目錄 | 互動式臨時容器，不是常駐服務；token 留在私有 volume。[說明](../../deploy/litellm/CHATGPT-OAUTH.md) |
| [apply-chatgpt-linux.sh](../../deploy/litellm/apply-chatgpt-linux.sh) | OAuth 路由接入／更新 | 以 base＋overlay 只重建 LiteLLM，不重建 PostgreSQL；也涉及 API 設定／服務，先排空工作與備份 |
| [fix-boot.sh](../../deploy/runtime-host/litellm-proxy/fix-boot.sh) | 為既有模型 proxy 加入開機等待／退避 | 先備份 drop-in，只 reload user manager 並確保 proxy started；不重啟已在運作的容器、不修改 image。新裝 Quadlet 已包含同等設定 |
| [fix-linux-exec-scope.sh](../../deploy/runtime-host/fix-linux-exec-scope.sh) | 修復 Agent exec cgroup 權限 | 主機特定修復，涉及 RuntimeHost／API；不是日常上版必要步驟 |
| [update-linux-scope-wrapper.sh](../../deploy/runtime-host/update-linux-scope-wrapper.sh) | 修復 wrapper 參數展開 | 備份並替換 wrapper、測試 stdin／檔案；不是 runtime image 換版器 |
| [start-ymir.sh](../../start-ymir.sh)／[start-ymir.ps1](../../start-ymir.ps1) | 各自既有 Docker／Windows 部署啟動 | **不適用目前 Ubuntu Podman 正式拓樸**；不代表已更新程式 |

### 已知版控缺口

目前 root 的 origin／production Tunnel Quadlet、retained-dependencies unit、正式 SQL 的原始建立規格，以及先前一次性 release 更新器，沒有完整的通用版控部署入口。`activate-linux-production.sh` 會操作部分既有主機配置，但不能憑空重建整台主機。

[`deploy/redeploy-ubuntu.sh`](../../deploy/redeploy-ubuntu.sh) 已於 2026-10-20 補強為這台主機的單一重新部署入口：API／Angular、實際 release 路徑的 RuntimeHost、可選已安裝主機服務、SQL 成套備份與受審查 migration、精確切換與失敗回復。先 `sudo bash deploy/redeploy-ubuntu.sh --dry-run`，再依[操作說明](ubuntu-redeploy.md)執行；本輪僅語法／隔離 mock 驗證，尚未用新腳本正式切換。基礎容器與既有 Agent 不重建，nginx／Tunnel 只依原狀暫停／恢復，**不是清空整台主機或自動首次安裝所有服務的工具**。上列主機專用配置的去機密來源、缺少服務的安裝與 Agent 個別換版仍是獨立工作；禁止把含密鑰設定／一次性更新包直接提交 Git。

## 3. 部署前：寫清楚這次要動什麼

每次從[流水帳範本](../deployments/README.md)建立紀錄，先填：

1. 經授權的主機／時段、來源 commit、CI URL 與結果。
2. **逐服務**列出更新／不變、舊／新 image ID 或 publish release、對應 script／配置差異。
3. 兩模組 migration 的實際差異、隔離 SQL 測試、相容性與回復策略；沒有 migration 就記「無」。
4. 排空方式、資料／keyring／workspace 一致性備份、私有備份位置、驗收項目與負責人。

日常更新範圍判斷：

| 變更 | 通常需要更新 | 不應連帶重建 |
|---|---|---|
| Angular 或 API 業務邏輯 | API image（含 Angular）；migration 視差異 | SQL／PostgreSQL、LiteLLM、nginx、Tunnel、所有 Agent |
| RuntimeHost 或被它引用的程式碼 | RuntimeHost publish release | 其他容器，除非安全／協定變更確實要求協調更新 |
| Pi／Agent 工具 | Agent image＋經審查的個別 runtime 替換計畫 | 全部 Agent 的強制移除；不得刪 workspace |
| 模型路由／OAuth | LiteLLM；模型選單／權限配置變更時協調 API | PostgreSQL／SQL；不要重設 master key／salt |
| nginx／Tunnel 配置 | 該入口服務 | API、資料庫與 Agent，除非另有明確變更 |
| 文件／開機 drop-in | 文件或指定 unit 配置 | 整站應用 image；程式碼 main 更新不代表必須重新部署所有服務 |

這是操作原則，不是目前已存在的自動 diff 計算功能；共用相依變更仍需逐服務判斷。

## 4. 首次安裝流程

**僅供新主機**：依 [Ubuntu 安裝總指南](ubuntu-install.md) 的完整命令與安全限制操作，下面只是順序索引，不是可直接執行的 script。

1. 工具／固定來源版本 → 專用帳號、subuid/subgid、linger、私有目錄與雙網路。
2. 建立帶持久 volume 的 SQL／PostgreSQL／LiteLLM，等待各自 readiness；保護 master key／salt。
3. 建立正式 DB／最小權限帳號，套兩模組 migration，核對 schema／history。
4. 建置 ymir store 的 Agent image；安裝 bridge → proxy 等待 socket → 驗證 Agent 模型路徑。
5. Publish RuntimeHost，配置 Unix socket／token、wrapper、權限，驗證 RuntimeHost health。
6. 建置 root store 的 API image（含 Angular）、設定 keyring／env／掛載，固定 image ID 後啟動。
7. 本機 health／登入／真對話／SSE／附件保存驗收通過後，才開 Tunnel；新主機與現有 nginx 拓樸二擇一，不能照抄混裝。
8. 可選服務逐項部署、逐項驗收；最後安排冷開機及備份還原測試。

## 5. 日常更新流程

以[安裝指南 §12](ubuntu-install.md#12-更新與回復)及[備份還原指南](backup-restore.md)為準；沒有腳本的步驟需明確寫在本次流水帳，不能當作已自動執行。

1. **準備**：乾淨 main 同步、選 CI 通過的 SHA，只建置受影響的 image／publish 到新版本目錄；保存舊版，不以浮動 tag 作唯一依據。
2. **審查**：明確列出將執行的 script、順序及影響服務，核對來源與本機假設。不要把上表所有修復 script 串起來執行。
3. **排程與鎖**：取得正式授權；與 MaintenanceHost 使用同一把 `/run/ymir-lifecycle/deploy.lock`。部分舊 script 自用 `/run/lock/ymir-production-v2.lock`，**不是同一把鎖**，不能假定已互斥；使用前先審查並協調鎖範圍。
4. **排空與備份**：依變更暫停入口／接受新工作、確認 Queued／Running 為零；需要一致性快照時停止對應寫入者。SQL 原生備份＋verify、keyring／workspace／knowledge／sites，以及涉及模型服務時的 PostgreSQL／OAuth／salt／secret 成套私有備份；不需為了備份而重建資料容器。
5. **Migration**：僅執行本次已審查的差異，明確設定 filtered index 的 SQL SET options；兩模組逐一檢查 exit code、history 與 schema，失敗就停止，不跳過測試或捏造 history。
6. **精確切換**：只更新目標服務的 image ID／release／配置；備份後才改動。root／user manager 分別 reload；system unit 與 user unit 不可只用 `After=` 假裝跨 manager 排序。
7. **依賴就緒**：先資料服務 → bridge／模型 proxy → RuntimeHost／API → 公開入口。啟動次序不代表一律要重啟這些服務；未變動且健康者保持運作。
8. **驗收**：核對精確版本、安全設定及掛載，API health、Agent 模型連線、公開登入頁、真登入／對話／SSE／成果檔案。上游真模型會計費，須經授權；liveliness 200 不代表真推論成功。
9. **失敗回復**：只回復本次變動的配置／image／release；相容 additive schema 可保留。不相容時依批准計畫還原成套資料，不能盲目 down migration 或覆蓋 DB。記錄入口是否仍關閉及資料風險。
10. **結案**：將實際命令／exit code、起訖版本、備份、驗收、回復結果寫入流水帳及 Sprint 看板；未執行的項目標「未驗證」，更新 CI 結果。

## 6. 開機順序與唯讀驗證

目前已知依賴：

```text
itadmin user manager → retained-dependencies → SQL／PostgreSQL／LiteLLM
system bridge → /run/ymir-litellm/litellm.sock
ymir linger／user manager → proxy ExecStartPre 等 socket → 模型入口
RuntimeHost socket＋SQL＋LiteLLM → API readiness → nginx → Tunnel → 公開驗收
```

這是依賴關係，不代表所有 readiness 都已被 systemd 完整編排。10/19 已套用 proxy 等待／10 秒退避；完整冷開機驗收仍未執行。

在**目前 Ubuntu 主機**讀回狀態（不是重新部署）：

```bash
systemctl is-active ymir-retained-dependencies.service ymir-litellm-bridge.service \
  ymir-runtime-host.service ymir-api.service ymir-origin-production.service \
  ymir-cloudflared-production.service
podman ps --format '{{.Names}}\t{{.Status}}'  # 此行須以 itadmin 執行
sudo -H -u ymir env XDG_RUNTIME_DIR=/run/user/$(id -u ymir) \
  systemctl --user show ymir-litellm-proxy.service \
  -p ActiveState -p WantedBy -p ExecStartPre -p RestartUSec -p StartLimitIntervalUSec
curl --fail --max-time 10 http://127.0.0.1:4000/health/liveliness
curl --fail --max-time 10 http://127.0.0.1:5080/health
```

代理連線還需在現存 Agent 內以無憑證的 liveliness 請求驗證；依本次經核對的容器名稱操作，不公開個人 ID。不要為健康檢查列印完整 env／container inspect、token、DB 字串。分享 journal 前須遮蔽機密與個人資料。

## 7. 更新這份紀錄的規則

- 新增／改名／移除容器或 script 時，同步更新第 2 節；主機專用檔案沒有版控來源就直接標明。
- 變更拓樸必須先有相應 ADR，不以更新本文件代替架構決策。
- 每次部署、開機修復或回復都在[流水帳](../deployments/README.md)新增獨立紀錄，不覆寫歷史；不把「準備完成」寫成「部署完成」。
- Secret、備份內容、OAuth token、個人資料、完整日誌不進 Git；只記去機密的索引、版本與結果。
