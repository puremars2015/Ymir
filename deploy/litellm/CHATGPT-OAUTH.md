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

## 第二階段：選定模型後接入（尚未啟用）

- 在 LiteLLM 掛載同一個 token volume（可寫），設定 `CHATGPT_TOKEN_DIR`，使用 `chatgpt/<已確認模型 ID>` 的獨立 alias；不覆蓋現有 OpenRouter 路由。
- OAuth 必須先完成，proxy startup 不得等待互動登入。只使用一個 ChatGPT 帳號／一份可寫 auth.json，避免 refresh token 輪替後多份憑證互相失效；初期使用單一 proxy worker。
- 先驗證 Pi 所需 Chat Completions→Responses 橋接、stream 與 tools，再加入 Ymir Models／AllowedModels；全域與個人權限沿用管理介面，沒有授權的使用者不得取得該模型 virtual key。
- token 只給 LiteLLM，絕不掛載到 API／RuntimeHost／Agent，不放進 .env 或映像。重啟時保留 volume；不用唯讀 Secret 當 refresh token 寫入位置。

## 共用帳號與配額

使用者已選擇讓所有獲授權的 Ymir 使用者可使用此 provider；所有人共用同一登入帳號及 workspace 的訂閱額度，並非每個人帶自己的 ChatGPT 訂閱。使用者需確認訂閱方案允許其預定用途。

LiteLLM 可按 virtual key／user 記 token，但訂閱 provider 的 spend 可能為 0，**美元 max_budget 不代表訂閱配額限制**。需配合部署的路由 RPM／TPM、Ymir 執行次數／併發限制與供應商帳號配額；不得宣稱無限額度或等同 API 付費帳單。

登入後尚需核對可用模型、完成橋接驗證與正式部署。此階段不修改既有模型清單、不重啟目前服務。
