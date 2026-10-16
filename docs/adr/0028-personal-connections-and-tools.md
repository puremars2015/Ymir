# ADR-0028：個人連接與工具管理

- 狀態：已採納（2026-10-15 使用者同意建議並要求實作）
- 補充：ADR-0012、ADR-0013、ADR-0022、ADR-0026
- 修訂：[ADR-0030](0030-plugin-marketplace-and-onedrive-agent-tools.md)（入口改為外掛市集 `/plugins`；§7 改為啟用 OneDrive 外掛後由 Agent 自行存取）

## 決策

1. 新增「個人設定 → 連接與工具」，分開顯示平台支援的 Connector（首批 OneDrive）及個人 MCP；不開放任意 Connector、stdio 命令、程式或套件上傳。原個人設定保留導覽入口。
2. 個人 MCP 首批為 HTTPS streamable HTTP，允許新增、測試 initialize、啟停及移除；只接受 HTTPS 443、無 userinfo／query／fragment 的 URL。Bearer token 選填，寫入專用 `vibemaker.personal_mcp_servers` 表前以使用者綁定的 Data Protection purpose 加密，不回顯、不寫入 prompt／稽核／設定檔／程序參數。
3. 管理端仍控制 `mcp` 與 `internet`；新增、啟用、測試與執行都要求兩者允許。撤銷能力後仍可查看摘要、停用及移除。每人最多 20 個註冊，名稱唯一；異動只稽核使用者及註冊 id，不記名稱、URL、token 或遠端回應。
4. API 連線測試只發送 MCP initialize，不呼叫工具。獨立 HTTP transport 禁用 proxy／redirect／cookie，DNS 解析後拒絕任何非公開 IP，socket 綁定到已驗證的 IP（TLS 仍驗證原 hostname），限制連線／整體逾時及回應大小。不能透過 API 探測 localhost、內網、metadata、DNS rebinding 或跟隨重新導向。私網 MCP 不屬於本批 API 測試範圍。
5. 個人執行時，把啟用且有權限的註冊合併進 Pi MCP 設定；個人項目使用 id 衍生名稱，平台項目最後合併且優先。Bearer token 只用每次程序的專用環境變數，設定檔只含引用。原 `mcp.user.json` 保留，不移除 Agent 自建項目。
6. 不把個人註冊或憑證帶到群組共用 runtime；不讓管理員查看個人 token。使用者自己的 MCP 憑證會進入其個人 runtime，與平台憑證邊界不同，UI 必須告知；不能承諾 Agent 無法接觸該程序環境。停用／移除只影響下次執行，不終止已執行的程序或撤銷遠端 token；若懷疑外洩，必須到服務端撤銷 token。
7. Connector 清單僅顯示平台已實作的連接，沿用 OneDrive OAuth／權限／解除流程。連接帳號不代表將整個雲端交給 Agent；選定項目及寫回限制仍依 ADR-0022。

## 交付與限制

新增 migration、授權矩陣、加密／隔離／撤銷／SSRF／無秘密輸出與前端回歸測試。實作與正式部署分開；需要先備份、套用 migration 並更新 API／前端。OAuth MCP discovery、任意 headers、編輯 URL、私網探測、stdio 與新增 Connector provider 留待後續。
