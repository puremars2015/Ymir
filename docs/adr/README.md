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
| [0013](0013-onedrive-connector.md) | OneDrive connector：後端經 Microsoft Graph 同步，Agent 不持有憑證 | 已採納 |
| [0014](0014-rag-knowledge-base.md) | RAG 知識庫：專案文件索引、經 LiteLLM 的 Embedding、每專案一份 SQLite | 已採納 |
| [0015](0015-explicit-execution-deliverables.md) | 交付成果與工作檔案分離 | 已採納 |
| [0016](0016-site-hosting.md) | 網站託管：獨立網域、SiteHost 服務、票據登入與分享 | 已採納 |
| [0017](0017-execution-thinking-depth.md) | 每次執行保存思考深度 | 已採納（固定深度清單由 ADR-0018 取代） |
| [0018](0018-model-specific-thinking-capabilities.md) | 依模型與傳輸 API 提供思考選項 | 已採納 |
| [0019](0019-user-model-access.md) | 個別使用者模型權限：動態繼承、允許與禁止 | 已採納 |
| [0020](0020-admin-model-access.md) | 管理員控制開放模型（原誤用 0016，2026-10-13 改號） | 已採納（個人權限由 ADR-0019 延伸） |
| [0021](0021-per-user-model-usage.md) | 每人每模型用量（讀 LiteLLM model_groups，Ymir 不保存） | 已採納 |
| [0022](0022-onedrive-linked-files.md) | 讓 Agent 處理使用者指定的 OneDrive 檔案（含刪除、移動、改名） | 手動連結已被 ADR-0030 取代 |
| [0023](0023-user-direct-messages.md) | 使用者之間的一對一訊息（右側成員名單、SSE 即時通知） | 已採納 |
| [0024](0024-google-account-sso.md) | Google 帳號 SSO 登入 | 已採納 |
| [0025](0025-ymir-resource-monitoring-and-reclamation.md) | Ymir 資源監控與安全自動回收 | 已採納 |
| [0026](0026-group-chat-and-group-agent.md) | 群組聊天與群組 Agent（@bot） | 已採納 |
| [0027](0027-admin-user-management-and-guest-role.md) | 管理員的使用者管理（改名、角色、刪除）與訪客角色 | 已採納 |
| [0028](0028-personal-connections-and-tools.md) | 個人連接與工具管理（OneDrive 與 HTTP MCP） | 已採納 |
| [0029](0029-make-tools-sidebar.md) | 左側「小工具」：/make 小工具自動發布並在 Ymir 內以 iframe 開啟 | 已採納 |
| [0030](0030-plugin-marketplace-and-onedrive-agent-tools.md) | 外掛市集與 OneDrive Agent 工具 | 已採納 |
| [0031](0031-llm-auto-model-router.md) | 以 LLM 自動路由選擇回覆模型（Auto：輕／中／重，預設啟用） | 已採納（待實作） |

新增 ADR 時複製以下格式：

```markdown
# ADR-XXXX：標題

- 狀態：提議中 / 已採納 / 已被 ADR-YYYY 取代
- 日期：YYYY-MM-DD

## 背景
## 決策
## 影響
```

- [ADR-0015：交付成果與工作檔案分離](0015-explicit-execution-deliverables.md)
