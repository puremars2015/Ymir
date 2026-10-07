# Architecture Decision Records

每個重要的架構決策一份檔案，編號遞增、不刪除；決策被取代時把狀態改為「已被 ADR-XXXX 取代」並新增一份 ADR。

| 編號 | 標題 | 狀態 |
|---|---|---|
| [0001](0001-modular-monolith-and-naming.md) | 模組化單體與命名（Ymir / Vibe Maker） | 已採納 |
| [0002](0002-bff-cookie-auth-for-sse.md) | 以 BFF + Cookie 做認證，讓 SSE 可直接使用 | 已採納 |
| [0003](0003-pi-rpc-via-podman-exec.md) | 透過 `podman exec` 以 Pi RPC 模式執行 Agent | 已採納 |
| [0004](0004-litellm-virtual-keys.md) | Container 內使用 LiteLLM Virtual Key | 已採納 |
| [0005](0005-docker-for-development.md) | Docker 作為開發 / 驗證用的 container engine | 已採納 |
| [0006](0006-cloudflare-tunnel-public-edge.md) | 以 Cloudflare Tunnel 作為 Ymir 平台的對外入口 | 已採納 |
| [0007](0007-one-runtime-per-user.md) | 一個使用者一個 Runtime，專案是 Runtime 內的檔案群組 | 已採納 |
| [0008](0008-containerized-api-runtime-host.md) | API 放進容器，Agent runtime 由主機上的 runtime host 管理 | 已採納 |
| [0009](0009-entra-id-and-local-accounts.md) | 企業帳號（Entra ID OIDC）與本機帳號密碼登入 | 已採納 |
| [0010](0010-admin-editable-system-settings.md) | 管理介面與可由網頁修改的系統設定（Entra ID、Cloudflare Tunnel） | 已採納 |
| [0011](0011-runtime-lifecycle-policy.md) | Runtime 生命週期與執行政策（閒置停止、對帳、配額、用量） | 已採納 |
| [0012](0012-agent-extensions-and-platform-mcp.md) | Agent 擴充能力：管理員管制的使用者自建擴充，與開發人員維護的平台 MCP | 已採納（egress 由管理員控制，預設允許） |

新增 ADR 時複製以下格式：

```markdown
# ADR-XXXX：標題

- 狀態：提議中 / 已採納 / 已被 ADR-YYYY 取代
- 日期：YYYY-MM-DD

## 背景
## 決策
## 影響
```
