# 平台 MCP 與 MCP Gateway（ADR-0012 B）

Agent 只能經 **Ymir MCP Gateway** 使用平台服務（公司內部系統、知識庫等）。gateway 是獨立的服務（`src/Ymir.McpGateway`），
它：

- 驗證 API 每次執行前簽發的**每人短期 token**（使用者、可用服務、期限 ≤ 1 小時）；
- 依服務目錄把 `/mcp/{服務}` 轉送到後端，並換成 gateway 自己保管的後端憑證；
- 每人 rate limit、逾時，並為每次工具呼叫寫稽核（使用者、服務、工具名稱；不含參數與回傳內容）。

Agent container 裡只有 token 的環境變數 `YMIR_MCP_TOKEN`（`mcp.json` 只寫 `${YMIR_MCP_TOKEN}` 引用），沒有任何後端位址或憑證。

## 服務目錄（開發人員）

`deploy/mcp/servers.json`（版控，經 PR 審查上線）：

```json
{ "servers": [ { "name": "eip-docs", "description": "查詢公司文件", "url": "http://eip-mcp.internal:8080/mcp", "credentialEnv": "EIP_MCP_TOKEN" } ] }
```

- `name`：小寫英數字與 `-`，是 gateway 路徑與 Agent 看到的服務名稱。
- `description`：Agent 依這段說明判斷何時使用。
- `url`：後端的 streamable HTTP MCP 端點，只有 gateway 使用。
- `credentialEnv`：後端憑證所在的環境變數名稱（值只放在 gateway 的 `/etc/ymir/mcp-gateway.env`）。

範例見 `servers.example.json`。管理員在「管理 → 系統設定 → 平台 MCP 服務」只能啟用 / 停用與設定可用對象，不能新增服務或位址；新服務預設停用。

## 部署

1. 建立帳號與目錄：`useradd --system ymir-mcp`、`/opt/ymir/mcp-gateway`（`dotnet publish src/Ymir.McpGateway -c Release -o ...`）、`/opt/ymir/mcp/servers.json`。
2. 產生簽章金鑰 `openssl rand -hex 32`，寫入：
   - gateway：`/etc/ymir/mcp-gateway.env` 的 `McpGateway__TokenSigningKey`（範本 `mcp-gateway.env.example`）；
   - API：`deploy/api/.env` 的 `Ymir__Mcp__TokenSigningKey`，並設定 `Ymir__Mcp__GatewayUrl`（**Agent container 看得到的位址**）與 `Ymir__Mcp__CatalogPath`（API 容器內的目錄檔路徑，唯讀掛載同一份 `servers.json`）。
3. 安裝 `ymir-mcp-gateway.service`，`systemctl enable --now ymir-mcp-gateway`。
4. 網路：Agent container 必須連得到 gateway——包含管理員關閉對外連線時使用的受限網路（ADR-0012 A.8，`--internal` 網路上要有 gateway 的位址，例如把 gateway 以 container 方式接上同一個網路，或在受限網路的閘道位址上監聽）。gateway 自己要連得到後端服務；Agent container 不需要也不應該連得到後端。

沒有設定 `Ymir__Mcp__GatewayUrl` 時，平台 MCP 整個停用（不影響其他功能）。

## 本機驗證

```bash
FAKE_MCP_TOKEN=dev-backend-secret dotnet run --project tests/Ymir.Testing.FakeMcp     # http://127.0.0.1:5320/mcp，echo 工具
YMIR_MCP_ECHO_TOKEN=dev-backend-secret dotnet run --project src/Ymir.McpGateway -- \
  --urls http://127.0.0.1:5310 --McpGateway:CatalogPath=deploy/mcp/servers.example.json \
  --McpGateway:TokenSigningKey=dev-mcp-signing-key-0123456789abcdef --ConnectionStrings:ymir="<同 API>"
# API 加上 Ymir__Mcp__GatewayUrl=http://127.0.0.1:5310、Ymir__Mcp__CatalogPath、Ymir__Mcp__TokenSigningKey
cd web && CHROMIUM_PATH=/opt/pw-browsers/chromium npm run e2e:mcp -- <截圖目錄>
```

**未驗證、待使用者環境確認**：真實後端服務（EIP / MES）、正式主機上受限網路連到 gateway 的路由。
