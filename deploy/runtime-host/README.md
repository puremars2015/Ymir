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
| `VibeMaker__Runtime__*` | Agent container 的設定（Provider、WorkspaceRoot、Image、資源限制、Network、SelinuxRelabel），與原本 API 的設定相同 |

- Runtime host 的 `Provider` 只能是 `Podman`、`Docker`（開發 / 驗證）或 `Local`（Development 環境才允許）；設成 `Remote` 會拒絕啟動。
- 回給 API 的錯誤只有摘要；詳細內容（stderr、podman 錯誤）只寫在 runtime host 的 log。
- 程序的環境變數（例如 LiteLLM virtual key）只以名稱傳給 `podman exec --env NAME`，值不會出現在程序參數或 log 中。

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
