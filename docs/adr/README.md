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

新增 ADR 時複製以下格式：

```markdown
# ADR-XXXX：標題

- 狀態：提議中 / 已採納 / 已被 ADR-YYYY 取代
- 日期：YYYY-MM-DD

## 背景
## 決策
## 影響
```
