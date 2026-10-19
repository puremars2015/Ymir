# Ymir runtime host（主機服務）

架構決策見 [ADR-0008](../../docs/adr/0008-containerized-api-runtime-host.md)。

```
瀏覽器 ─HTTPS─> Cloudflare ─> cloudflared ─> 127.0.0.1:5080  Ymir API（container，host network）
                                                   │  Unix socket /run/ymir-runtime/runtime.sock + token
                                                   ▼
                                     Ymir.RuntimeHost（主機上，帳號 ymir）
                                                   │  rootless podman run / exec
                                                   ▼
                                     ymir-user-{userId}（每位使用者一個 Agent container，ADR-0007）
```

- **API 容器**：不執行 Podman，也不掛載任何 container runtime socket。
- **Runtime host 只接受兩種請求**：
  - 為 user X 確保 runtime；
  - 在 user X 的 runtime 內執行程序。
- 以下都由 runtime host 自己的設定決定，API 無法指定：
  - image、掛載、資源限制；
  - host 路徑：`{WorkspaceRoot}/users/{userId}`。

## 安裝（Linux，需要 root）

> 以下步驟在雲端開發沙箱無法驗證（沒有可用的 rootless Podman 主機），**待使用者環境確認**。

在 Linux 的 repo 根目錄執行；建置需 .NET 10 SDK，主機執行 RuntimeHost 需 ASP.NET Core 10 runtime。此處先完成主機服務與 rootless Agent 準備，再依 [API 首次部署指南](../api/README.md#linuxrootful-podman--quadlet) 安裝 rootful API 容器。

1. **建立帳號與 group**：
   - `ymir` 跑 rootless Podman；
   - `ymir-runtime` 是 socket 的 group，API 容器用它連線。
   ```bash
   sudo groupadd --system ymir-runtime
   sudo useradd --create-home --shell /usr/sbin/nologin ymir
   sudo usermod --add-subuids 100000-165535 --add-subgids 100000-165535 ymir   # 若 /etc/subuid 還沒有 ymir
   sudo loginctl enable-linger ymir     # 沒有登入 session 也保留 /run/user/<uid>
   ```
2. **資料目錄**：只有 `ymir` 可以存取。
   ```bash
   sudo install -d -o ymir -g ymir -m 700 /srv/ymir/workspaces
   ```
3. **建置 Agent runtime image**，以 `ymir` 帳號建置，image 存在它的 rootless 儲存區：
   ```bash
   sudo -u ymir -H env XDG_RUNTIME_DIR=/run/user/$(id -u ymir) podman build --build-arg PI_VERSION=1.0.0 -t localhost/ymir/agent-runtime:1.0.0 -f runtime/agent/Containerfile runtime/agent
   ```
4. **發行 runtime host**：主機需要 ASP.NET Core 10 runtime。
   ```bash
   dotnet publish src/Ymir.RuntimeHost -c Release -o /tmp/ymir-runtime-host
   sudo install -d /opt/ymir/runtime-host && sudo cp -r /tmp/ymir-runtime-host/. /opt/ymir/runtime-host/
   ```
5. **設定**：
   ```bash
   sudo install -d -m 755 /etc/ymir
   sudo install -m 600 deploy/runtime-host/runtime-host.env.example /etc/ymir/runtime-host.env
   sudoedit /etc/ymir/runtime-host.env
   ```
   - 首次安裝才複製範本，更新既有部署時保留原設定。
   - 產生 token：`openssl rand -hex 32`。同一值填入此檔的 `RuntimeHost__Token` 與 `/etc/ymir/api.env` 的 `VibeMaker__Runtime__Remote__Token`（Docker Compose 才使用 `deploy/api/.env`）。
   - 執行 `id -u ymir`，將實際數字填入 `XDG_RUNTIME_DIR=/run/user/<uid>`；env 檔不會替你執行 `$(id -u ymir)`。
   - 保留 `Provider=Podman`、Unix socket、workspace 與 Agent image 設定；不使用 Tunnel 管理時，將 `RuntimeHost__Tunnel__Mode` 設為 `Disabled`。
6. **啟動**：
   ```bash
   sudo cp deploy/runtime-host/ymir-runtime-host.service /etc/systemd/system/
   sudo systemctl daemon-reload
   sudo systemctl enable --now ymir-runtime-host.service
   sudo journalctl -u ymir-runtime-host.service -n 100 --no-pager
   ```
7. **驗證**：
   ```bash
   sudo -u ymir curl --unix-socket /run/ymir-runtime/runtime.sock http://localhost/health        # ok
   sudo -u ymir curl -i --unix-socket /run/ymir-runtime/runtime.sock -X POST \
     http://localhost/v1/users/00000000000000000000000000000001/runtime                            # 401（沒有 token）
   ls -l /run/ymir-runtime/                                                                         # srw-rw---- ymir ymir-runtime runtime.sock
   ```

## 設定

### Ubuntu：Agent exec 的 cgroup 權限錯誤

若 RuntimeHost 的 system service 可以建立 Agent 容器，但執行 `bash`／`mkdir` 時報 `cgroup.procs: Permission denied`、exit code 126，可能是 RuntimeHost 位於 `system.slice`，rootless Podman 容器位於 `ymir` 的 user manager subtree，cgroup v2 不允許跨未授權的共同父層遷移程序。僅有 `Delegate=yes` 不代表這兩個 subtree 之間也取得授權。

目前主機的修復工具：

```bash
sudo bash deploy/runtime-host/fix-linux-exec-scope.sh
```

腳本先用 `systemd-run --user --scope --quiet` 驗證現存 Agent 的 uid／Pi、stdin 和環境變數；全部通過且沒有排隊／執行中的工作後，備份 RuntimeHost env，安裝 root-owned `ymir-podman-scope` wrapper，設定 `VibeMaker__Runtime__ContainerExecutable` 並重啟 RuntimeHost。從 system service cgroup 再驗證 wrapper；設定 `RuntimeDirectoryPreserve=yes` 保留 API bind-mount 的 socket 目錄，並重啟 API 刷新舊掛載，等待整體 health=Healthy 再啟動 nginx／Tunnel。失敗時嘗試回復原 env／drop-in；不刪除容器或使用者檔案，不執行付費模型。

這個修復保留 rootless Podman、CPU／memory／pids 限制、keep-id、唯讀檔案系統及既有 capabilities 政策；不要用 privileged、停用 cgroups、廣泛 chmod／chown `/sys/fs/cgroup` 來繞過問題。供應商 key 不寫進 scope 命令參數。

wrapper 必須使用 `--expand-environment=no`，讓 `bash -c` 的 `${1##*/}`／`${1%/*}`／`$1` 等參數原樣進入容器；否則 systemd 會誤展開寫檔腳本，造成附件寫入失敗，並被現有 API 顯示成「檔名不合法或是空檔案」。已安裝早期 wrapper 的目前主機可熱更新，不需重啟服務或容器：

```bash
sudo bash deploy/runtime-host/update-linux-scope-wrapper.sh
```

工具先測試固定的 user scope，再備份並原子替換 root-owned wrapper，最後以實際 wrapper 重跑。`test-podman-scope.py` 使用原始 C# WriteScript，在現存 Agent 的唯一 `/tmp` 測試目錄驗證中文／空白檔名、二進位 stdin、大小與內容、symlink 防逃逸；不讀寫使用者 workspace、資料庫或付費模型。亦可在有 GNU coreutils／bash 的隔離測試容器執行 `python3 deploy/runtime-host/test-podman-scope.py <container>`。

| 設定 | 說明 |
|---|---|
| `RuntimeHost__Listen` | `unix:/run/ymir-runtime/runtime.sock`（建議）；或只限 loopback 的 `http://127.0.0.1:5090`（例如 Windows 開發機） |
| `RuntimeHost__Token` | 與 API 共用的 bearer token，至少 32 字元；只放在 `/etc/ymir/runtime-host.env` |
| `RuntimeHost__SocketMode` | socket 權限（八進位），預設 `660`；不允許 other 存取 |
| `RuntimeHost__Tunnel__Mode` | `SystemdUser`：由管理介面設定 Cloudflare Tunnel token（ADR-0010）；`Disabled`（預設）：不開放 |
| `RuntimeHost__Tunnel__EnvFile` / `Unit` | 預設 `~ymir/.config/ymir/cloudflared.env`、`ymir-cloudflared.service`，一般不需要改 |
| `VibeMaker__Runtime__*` | Agent container 的設定（Provider、WorkspaceRoot、Image、資源限制、Network、RestrictedNetwork、SelinuxRelabel），與原本 API 的設定相同 |

- Runtime host 的 `Provider` 只能是 `Podman`、`Docker`（開發 / 驗證）或 `Local`（Development 環境才允許）；設成 `Remote` 會拒絕啟動。
- 回給 API 的錯誤只有摘要；詳細內容（stderr、podman 錯誤）只寫在 runtime host 的 log。
- 程序的環境變數（例如 LiteLLM virtual key）只以名稱傳給 `podman exec --env NAME`，值不會出現在程序參數或 log 中。

## 受限網路：讓管理員可以關閉 Agent 的對外連線（ADR-0012 A.8）

管理員可以在「管理 → 系統設定 → Agent 擴充能力」與「使用者」頁關閉 Agent 的對外連線（預設允許）。被關閉的成員，其 Agent container 會改接到一個 `--internal` network：只連得到同一個 network 上的 LiteLLM 與 MCP Gateway，連不到網際網路與內網。**沒有設定受限網路時，被關閉的成員無法執行 Agent**（不會退回成可以對外連線）。

做法：建立兩個 network，讓 LiteLLM（與之後的 MCP Gateway）**同時**接在兩個 network 上，Agent 用同一個名稱（`litellm`）連線，不論哪種模式都不用改模型位址。

1. 以 `ymir` 帳號建立 network（rootless Podman）：
   ```bash
   sudo -u ymir XDG_RUNTIME_DIR=/run/user/$(id -u ymir) podman network create ymir-agents-net          # 可以對外連線
   sudo -u ymir XDG_RUNTIME_DIR=/run/user/$(id -u ymir) podman network create --internal ymir-agents  # 受限
   ```
2. LiteLLM 以 container 接上兩個 network，並以 `litellm` 為名稱（同一個 rootless Podman 帳號）：
   ```bash
   sudo -u ymir XDG_RUNTIME_DIR=/run/user/$(id -u ymir) podman network connect ymir-agents-net litellm
   sudo -u ymir XDG_RUNTIME_DIR=/run/user/$(id -u ymir) podman network connect ymir-agents litellm
   ```
   如果 LiteLLM 原本用 `deploy/litellm` 的 compose 在其他帳號或主機上執行，需要改成在 `ymir` 帳號以 container 執行，或在兩個 network 上放一個只轉送到 LiteLLM 的 proxy container（見下方「LiteLLM 在其他帳號執行」，目前正式主機採用這種做法）。
3. `/etc/ymir/runtime-host.env`：
   ```bash
   VibeMaker__Runtime__Network=ymir-agents-net
   VibeMaker__Runtime__RestrictedNetwork=ymir-agents
   ```
   API 的 `VibeMaker__Pi__ModelBaseUrl` 設為 `http://litellm:4000/v1`（LiteLLM 的 port）。重啟 runtime host。
4. 驗證：管理員把某位成員的「對外連線」設為不允許 → 該成員送一則訊息（container 會以受限網路重建，檔案保留，稽核 `runtime.recreate`）→
   ```bash
   sudo -u ymir XDG_RUNTIME_DIR=/run/user/$(id -u ymir) podman inspect ymir-user-<userId> --format '{{index .Config.Labels "ymir.network"}}'   # restricted
   sudo -u ymir XDG_RUNTIME_DIR=/run/user/$(id -u ymir) podman exec ymir-user-<userId> node -e "fetch('https://example.com').then(()=>console.log('reachable'),e=>console.log('blocked',e.cause?.code))"   # blocked
   ```

- `RestrictedNetwork` 必須是專用的具名 network；設成 `host`、`bridge`、`slirp4netns`、`none` 等會拒絕啟動。名稱只來自這裡的設定，API 只傳「允許 / 不允許」。
- `--internal` network 的閘道 IP（host 端）上聽 `0.0.0.0` 的服務也連得到：主機上的服務請只綁需要的介面，或另設防火牆。
- 管理員變更政策不會中斷執行中的 Agent；在成員的下一次執行前才重建 container。
- 沙箱（root Podman）已實測：受限時 Agent 連得到 LiteLLM、連不到外網，Pi session 在重建後保留；**rootless Podman 的實際行為未驗證，待使用者環境確認**。

### LiteLLM 在其他帳號執行：Unix socket bridge + proxy container

LiteLLM 以 `deploy/litellm` 的 compose 在其他帳號（例如 `itadmin`）執行、只聽主機 `127.0.0.1:4000` 時，Agent container 連不到它。做法是兩段轉送，檔案在 [`litellm-proxy/`](litellm-proxy/)：

```
Agent ─http://litellm:4000─> ymir-litellm-proxy（nginx，ymir 帳號，接 ymir-agents-net + ymir-agents）
      ─/run/ymir-litellm/litellm.sock─> ymir-litellm-bridge（socat，systemd）─> 127.0.0.1:4000 LiteLLM
```

1. Bridge（root，需要 `socat`）：
   ```bash
   sudo install -m 644 deploy/runtime-host/litellm-proxy/ymir-litellm-bridge.service /etc/systemd/system/
   sudo systemctl daemon-reload && sudo systemctl enable --now ymir-litellm-bridge
   ```
2. Proxy（`ymir` 帳號的 rootless Quadlet）：先以 `ymir` 帳號拉取 nginx 映像，把 `ymir-litellm-proxy.container` 的 `Image=` 換成它的 image ID。
   ```bash
   sudo install -d -o ymir -g ymir -m 700 /home/ymir/.config/ymir /home/ymir/.config/containers/systemd
   sudo install -o ymir -g ymir -m 600 deploy/runtime-host/litellm-proxy/litellm-nginx.conf /home/ymir/.config/ymir/litellm-nginx.conf
   sudo install -o ymir -g ymir -m 600 deploy/runtime-host/litellm-proxy/ymir-litellm-proxy.container /home/ymir/.config/containers/systemd/
   sudo -u ymir XDG_RUNTIME_DIR=/run/user/$(id -u ymir) systemctl --user daemon-reload
   sudo -u ymir XDG_RUNTIME_DIR=/run/user/$(id -u ymir) systemctl --user start ymir-litellm-proxy
   ```
3. API 的 `VibeMaker__Pi__ModelBaseUrl=http://litellm:4000/v1`（同上方第 3 步）。

**開機競態防護**：bridge 是 system service，proxy 是 `ymir` 的 user service；兩者的 `After=` 不會跨 systemd manager 排序。2026-10-17 曾因 proxy 先啟動、`/run/ymir-litellm` 尚未建立而連續失敗五次，之後永久停在 failed；API 的模型請求只回 `Connection error`。

新版 Quadlet 在建立容器前等待 socket（每輪最多 60 秒），失敗每 10 秒重試、不受快速失敗次數上限阻斷。這只代表 bridge socket 已存在，不代表上游模型已就緒；LiteLLM 與真模型仍需另做健康檢查。不要用 user service 的 `After=ymir-litellm-bridge.service` 假裝建立跨 manager 依賴。

既有主機以 drop-in 修正，**不覆蓋目前的 image、網路、nginx 或 secret，不重啟正在運作的容器**：

```bash
sudo bash deploy/runtime-host/litellm-proxy/fix-boot.sh
```

腳本要求既有 bridge enabled／active、socket 存在及 `ymir` linger=yes；先備份同名 drop-in 到 `/var/backups/ymir-litellm-boot-*`，再 reload user manager 並確保 proxy 已啟動。失敗時保留備份與錯誤供診斷，不自動停止現有服務。回復時只還原該次備份的 `90-boot-readiness.conf`（若 `previously-absent` 則僅移除這個新增檔），再以 `ymir` 執行 `systemctl --user daemon-reload`；不需要覆寫 Quadlet 或重啟 API。

驗證：`python3 deploy/runtime-host/litellm-proxy/test-boot.py`；正式主機需再從現存 Agent 呼叫 `http://litellm:4000/health/liveliness`。完整重新開機驗收應安排維護時段，不由修復腳本自動 reboot。

**`client_max_body_size` 必須保留**：Agent 每次呼叫模型都送出完整對話歷史（含 base64 圖片），nginx 預設上限只有 1 MB。沒有這行時，對話一超過 1 MB 就**每則都失敗**，使用者只看到「模型發生錯誤，請稍後再試」，API log 為 `ended with model error: 413 <html>…Request Entity Too Large…nginx`，而 LiteLLM 的 log 沒有對應的請求（2026-10-14 正式主機實際發生）。設定 `100m`：附件單檔上限 50 MB，base64 後約 1.33 倍。

既有主機補上設定（先備份）。設定檔是以**單一檔案** bind mount 進 container，`sed -i` 會換成新檔案（新 inode），container 內仍看到舊檔，`nginx -s reload` **不會生效**；必須重啟 container（模型呼叫中斷數秒），並確認 container 內的設定：

```bash
sudo bash -c 'cd /tmp; F=/home/ymir/.config/ymir/litellm-nginx.conf
cp -a $F $F.bak-$(date +%Y%m%d-%H%M%S)
grep -q client_max_body_size $F || sed -i "0,/access_log off;/s//access_log off;\n client_max_body_size 100m;\n proxy_request_buffering off;/" $F
U="sudo -u ymir XDG_RUNTIME_DIR=/run/user/$(id -u ymir)"; $U systemctl --user restart ymir-litellm-proxy
$U podman exec ymir-litellm-proxy grep -n client_max_body_size /etc/nginx/nginx.conf'
```

## Cloudflare Tunnel 由管理介面設定（ADR-0010）

runtime host 以 `ymir` 帳號執行，cloudflared 也改成同一個帳號的 rootless Podman Quadlet user service，所以 runtime host 不需要 root 就能寫入 token 並重啟：

1. 安裝 Quadlet（一次）：
   ```bash
   sudo -u ymir mkdir -p ~ymir/.config/containers/systemd ~ymir/.config/ymir
   sudo install -o ymir -g ymir -m 644 deploy/cloudflared/ymir-cloudflared.container ~ymir/.config/containers/systemd/
   sudo -u ymir XDG_RUNTIME_DIR=/run/user/$(id -u ymir) systemctl --user daemon-reload
   ```
2. `/etc/ymir/runtime-host.env` 設定 `RuntimeHost__Tunnel__Mode=SystemdUser`，重啟 runtime host：`sudo systemctl restart ymir-runtime-host`。
3. 用 Admin 登入 Ymir →「管理 → 系統設定 → 對外連線」貼上 token，按「套用 token」。狀態變成「已連線」即完成。
4. 如果原本用 `deploy/cloudflared/compose.yml` 跑 cloudflared，先停掉（`docker compose down`），並刪除 repo 目錄裡的 `.env`，避免同一個 tunnel 跑兩份、token 留在 repo 目錄。

- token 只存在 `~ymir/.config/ymir/cloudflared.env`（600）與 cloudflared container 內；API、資料庫、log 都沒有。
- 檢查：`sudo -u ymir XDG_RUNTIME_DIR=/run/user/$(id -u ymir) systemctl --user status ymir-cloudflared`。

## 開發機

- 不需要 runtime host：Development 環境的 API 預設是 `Local` runtime。在 Windows 上，API 跑在主機並使用 `Provider=Docker`。
- **Windows + Docker Desktop 驗證「API 在容器內」**：runtime host 聽 `http://127.0.0.1:5090`，API 容器經 `host.docker.internal:5090` 連線。步驟見 [`deploy/api/README.md`](../api/README.md#windowsdocker-desktop開發--驗證)。
- 想在本機驗證「API 在容器內」：
  ```bash
  dotnet run --project src/Ymir.RuntimeHost -- --environment Development \
    --RuntimeHost:Listen=unix:/tmp/ymir-runtime/runtime.sock --RuntimeHost:Token=<32 字元以上> \
    --VibeMaker:Runtime:Provider=Docker
  ```
  API 設定：
  - `VibeMaker__Runtime__Provider=Remote`
  - `VibeMaker__Runtime__Remote__Endpoint=unix:/tmp/ymir-runtime/runtime.sock`
  - `VibeMaker__Runtime__Remote__Token=<同上>`
