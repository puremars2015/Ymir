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
   sudo -u ymir -H podman build --build-arg PI_VERSION=1.0.0 -t localhost/ymir/agent-runtime:1.0.0 -f runtime/agent/Containerfile runtime/agent
   ```
4. **發行 runtime host**：主機需要 .NET 10 runtime。
   ```bash
   dotnet publish src/Ymir.RuntimeHost -c Release -o /tmp/ymir-runtime-host
   sudo install -d /opt/ymir/runtime-host && sudo cp -r /tmp/ymir-runtime-host/. /opt/ymir/runtime-host/
   ```
5. **設定**：
   - 複製 [`runtime-host.env.example`](runtime-host.env.example) 到 `/etc/ymir/runtime-host.env`，權限 `chmod 600`，擁有者 root。
   - 產生 token：`openssl rand -hex 32`。這個值也要填進 API 的 `deploy/api/.env`。
   - 填入 `XDG_RUNTIME_DIR=/run/user/$(id -u ymir)`。
6. **啟動**：
   ```bash
   sudo cp deploy/runtime-host/ymir-runtime-host.service /etc/systemd/system/
   sudo systemctl daemon-reload && sudo systemctl enable --now ymir-runtime-host
   journalctl -u ymir-runtime-host -f
   ```
7. **驗證**：
   ```bash
   sudo -u ymir curl --unix-socket /run/ymir-runtime/runtime.sock http://localhost/health        # ok
   sudo -u ymir curl -i --unix-socket /run/ymir-runtime/runtime.sock -X POST \
     http://localhost/v1/users/00000000000000000000000000000001/runtime                            # 401（沒有 token）
   ls -l /run/ymir-runtime/                                                                         # srw-rw---- ymir ymir-runtime runtime.sock
   ```

## 設定

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
   如果 LiteLLM 原本用 `deploy/litellm` 的 compose 在其他帳號或主機上執行，需要改成在 `ymir` 帳號以 container 執行，或在兩個 network 上放一個只轉送到 LiteLLM 的 proxy container。
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
