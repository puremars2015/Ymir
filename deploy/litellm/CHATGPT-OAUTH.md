# ChatGPT 訂閱 OAuth（LiteLLM 1.103.2）

官方 provider：`chatgpt/`；[LiteLLM 說明](https://docs.litellm.ai/docs/providers/chatgpt)。這是 ChatGPT／Codex 訂閱登入，不是 OpenAI API key，也不是 Ymir 網站登入。

## 第一階段：先登入，再選模型

在目前執行 LiteLLM 的 rootless Podman 帳號（本機 `itadmin`）執行：

```bash
bash deploy/litellm/login-chatgpt.sh
```

依終端顯示的 **官方 `https://auth.openai.com/codex/device`** 網址登入並填入短效 device code。不要把密碼、device code、token 或 auth.json 貼進對話或 Git。帳號可能需要先在 ChatGPT Security 設定開啟 device code 登入；Business／Enterprise 需 workspace 管理者允許。

工具使用獨立容器，不傳入 LiteLLM master key、MiniMax／OpenRouter 金鑰或資料庫連線。token 存入 `ymir-chatgpt-auth` named volume，目錄 700、auth.json 600；模型目錄查詢結果亦存在該 volume。只查官方模型目錄，不進行推論。登入後再選擇已核對的模型 ID，不能直接用 OpenRouter 的 model ID 推測訂閱可用模型。

再次只查模型目錄：`bash deploy/litellm/login-chatgpt.sh --catalog-only`。沒有 auth.json 時會直接拒絕，不在背景啟動登入提示。若目錄端點不可用，保留登入結果但不加入任意模型。

模型目錄依 Codex client version 篩選；預設 `0.162.1`，已核對 [OpenAI 官方 release](https://github.com/openai/codex/releases/tag/rust-v0.162.1)。舊版 `0.89.0` 會令目前帳號回傳空清單，不代表 OAuth 失敗。將來可使用 `CHATGPT_CATALOG_CLIENT_VERSION=<major.minor.patch>` 更新查詢版本；不改 LiteLLM 版本、不改 OAuth 身份。`hide`／`hidden` 的內部模型不加入可選清單。

## 第二階段：選定模型後接入

本機已登入並核對可用目錄，使用者選擇 GPT-6.1 Sol、GPT-6 Luna、GPT-6 Astra。先在 repo 根目錄執行：

```bash
sudo bash deploy/litellm/apply-chatgpt-linux.sh
```

腳本檢查 headless OAuth／模型目錄、沒有 QUEUED／RUNNING 工作後，保留原六模型與預設、追加 API Models／AllowedModels，生成非機密的 `config.chatgpt.runtime.yaml`，使用 `compose.yml`＋`compose.chatgpt.yml` 僅重建 LiteLLM 代理、重啟 API／入口。SQL、PostgreSQL、使用者 workspace 與權限設定不變；錯誤時嘗試回復原 provider／API，token volume 保留。設定備份含 API 密鑰，不可分享。部署後需在「管理 → 開放模型」勾選三個新選項，不會偷偷覆寫既有全域或個人權限。

| Ymir／LiteLLM alias | 上游 | 顯示名稱（DisplayName） | Provider |
|---|---|---|---|
| `chatgpt-gpt-6.1-sol` | `chatgpt/gpt-6.1-sol` | GPT-6.1 Sol | ChatGPT OAuth |
| `chatgpt-gpt-6-luna` | `chatgpt/gpt-6-luna` | GPT-6 Luna | ChatGPT OAuth |
| `chatgpt-gpt-6-astra` | `chatgpt/gpt-6-astra` | GPT-6 Astra | ChatGPT OAuth |

管理介面顯示「GPT-6.1 Sol (ChatGPT OAuth)」；使用者的模型選單只顯示「GPT-6.1 Sol」，同時開放 OpenRouter 的同名模型時才加上 provider 區分（ADR-0020 補充）。重跑 `apply-chatgpt-linux.sh` 時，既有的 `minimax*`／`openrouter-*` 模型若還沒有 Provider（名稱也沒有括號），會補上 `minimax.io`／`OpenRouter`，已設定的不覆蓋。

初期各路由 6 RPM／64000 TPM，使用 usage-based-routing-v2 與 pre-call checks；超限請求應在到達上游前拒絕。單一 proxy worker、同一可寫 auth volume。日後重建代理必須保留兩份 compose 檔；原六模型重置工具在 OAuth 啟用時會拒絕執行，避免不小心移除路由。

思考參數使用 `reasoning.effort`：pinned 1.103.2 的 OpenAI Chat validator 不認得這些新 ID 的 xhigh／max，但 Responses JSON 能原樣傳送；離線橋接測試已核對 low／medium／high／xhigh／max。目錄的 ultra 目前不在 Ymir 契約，不開放、不降級替換；此訂閱目錄未宣告 none，不能照抄 OpenRouter Luna 的關閉思考選項。

SDK 預設 Codex instructions 寫死「based on GPT-5」，此 overlay 用 `CHATGPT_DEFAULT_INSTRUCTIONS` 改成不聲稱型號的中性指示，保留 Pi 傳入的 system instructions／工具，不讓 GPT-6 路由被要求自稱 GPT-5。

- 在 LiteLLM 掛載同一個 token volume（可寫），設定 `CHATGPT_TOKEN_DIR`，使用 `chatgpt/<已確認模型 ID>` 的獨立 alias；不覆蓋現有 OpenRouter 路由。
- OAuth 必須先完成，proxy startup 不得等待互動登入。只使用一個 ChatGPT 帳號／一份可寫 auth.json，避免 refresh token 輪替後多份憑證互相失效；初期使用單一 proxy worker。
- `chatgpt-proxy-guard.py` 只在 OAuth proxy 作為 sitecustomize 載入，阻止 pinned SDK 在 refresh 失敗後退回 device-code 登入／冷卻等待；cached token 和正常 refresh 保留，需重新登入時回 401。Guard 載入失敗會拒絕 proxy startup，不會悄悄失效。獨立互動登入容器不載入此 guard。
- 先驗證 Pi 所需 Chat Completions→Responses 橋接、stream 與 tools，再加入 Ymir Models／AllowedModels；全域與個人權限沿用管理介面，沒有授權的使用者不得取得該模型 virtual key。
- token 只給 LiteLLM，絕不掛載到 API／RuntimeHost／Agent，不放進 .env 或映像。重啟時保留 volume；不用唯讀 Secret 當 refresh token 寫入位置。

## 共用帳號與配額

使用者已選擇讓所有獲授權的 Ymir 使用者可使用此 provider；所有人共用同一登入帳號及 workspace 的訂閱額度，並非每個人帶自己的 ChatGPT 訂閱。使用者需確認訂閱方案允許其預定用途。

LiteLLM 可按 virtual key／user 記 token，但訂閱 provider 的 spend 可能為 0，**美元 max_budget 不代表訂閱配額限制**。需配合部署的路由 RPM／TPM、Ymir 執行次數／併發限制與供應商帳號配額；不得宣稱無限額度或等同 API 付費帳單。

離線測試使用假 OAuth 與本機 SSE，不接觸真實 token：`python test-chatgpt-oauth.py`（目錄／權限／env append）及 pinned image 中的 `python test-chatgpt-provider.py chatgpt-models.yaml`（文字／工具／串流／reasoning／RPM）。真實推論會消耗訂閱配額，需另外由使用者測試；目前只完成官方登入、目錄查詢及離線橋接驗證。
