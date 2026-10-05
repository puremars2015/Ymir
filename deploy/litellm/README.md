# LiteLLM sample（MiniMax 國際站）

Ymir 的模型入口是 LiteLLM（SA §19、[ADR-0004](../../docs/adr/0004-litellm-virtual-keys.md)）。這個 sample 用 **MiniMax 國際站（minimax.io）訂閱帳號**當上游，
另外附一個轉給 Fake LLM 的 `fake-model`，方便離線驗證。

| 檔案 | 用途 |
|---|---|
| `config.yaml` | LiteLLM 設定：`minimax`（`https://api.minimax.io/v1`，OpenAI 相容）、`fake-model` |
| `compose.yml` | LiteLLM proxy（`ghcr.io/berriai/litellm:v1.103.2`，只綁 `127.0.0.1:4000`）+ LiteLLM 專用 PostgreSQL（virtual key 需要，不對外開 port） |
| `.env.example` | 金鑰範本；複製成 `.env` 後填入（**`.env` 已被 `.gitignore` 排除，不得提交**） |
| `smoke-test.sh` | 對 proxy 送一則訊息 |

## 使用

```bash
cd deploy/litellm
cp .env.example .env        # 填入 MINIMAX_API_KEY，並依註解產生 LITELLM_MASTER_KEY、LITELLM_SALT_KEY、LITELLM_DB_PASSWORD
docker compose up -d
./smoke-test.sh minimax     # 走 MiniMax
./smoke-test.sh fake-model  # 走 Fake LLM（需先 dotnet run --project tests/Ymir.Testing.FakeLlm -- --urls http://0.0.0.0:5199）
```

不想用 container 時：`pip install "litellm[proxy]==1.103.2"`，然後 `set -a; . ./.env; set +a; litellm --config config.yaml --host 127.0.0.1 --port 4000`
（此時 `.env` 的 `FAKE_LLM_BASE` 改為 `http://127.0.0.1:5199/v1`）。

## 注意

- **模型名稱**：`config.yaml` 預設為 `openai/MiniMax-M2`。實際可用的模型依訂閱方案而定，請在 MiniMax 後台確認後修改。
- **金鑰**：
  - `MINIMAX_API_KEY` 只放在 LiteLLM，不得放進 Agent container。
  - Agent 之後會拿 LiteLLM 發的短效 virtual key（ADR-0004，Sprint 4）。
  - 金鑰外洩時，到 MiniMax 後台重新產生，再更新 `.env`。
- **`LITELLM_MASTER_KEY`**：呼叫 proxy 必須帶這個金鑰；沒帶或帶錯都會被拒絕。
- **`LITELLM_SALT_KEY`**：用來加密資料庫裡的金鑰，設定後不可更改。

## Ymir API 接 LiteLLM（每位使用者的 virtual key，ADR-0004）

Ymir API 用 master key 呼叫 LiteLLM 的 `/key/generate`，為每位使用者發一把 virtual key：
- 只能用指定模型；
- 預設 24 小時有效，到期前 1 小時換發，舊 key 會撤銷；
- 可設預算上限。

Agent container 只拿得到這把 virtual key，**master key 與 MiniMax key 都不會進 container**。

| 設定（環境變數） | 說明 |
|---|---|
| `VibeMaker__LiteLlm__BaseUrl` | API 連 LiteLLM 的位址，例如 `http://127.0.0.1:4000` |
| `VibeMaker__LiteLlm__MasterKey` | 與 `.env` 的 `LITELLM_MASTER_KEY` 相同；只放在 API 的 secret / 環境變數 |
| `VibeMaker__LiteLlm__AllowedModels__0` | key 可用的模型；未設定時只允許 `VibeMaker__Pi__ModelId` |
| `VibeMaker__LiteLlm__KeyLifetime` / `RenewBefore` / `MaxBudget` | 有效期（預設 `1.00:00:00`）、提前換發時間（預設 `01:00:00`）、每把 key 的預算上限（美元，可不設） |
| `VibeMaker__Pi__ModelBaseUrl` | **Agent container 內**連 LiteLLM 的位址：`http://host.containers.internal:4000/v1`（Podman）或 `http://host.docker.internal:4000/v1`（Docker） |
| `VibeMaker__Pi__ModelId` | `minimax`（對應 `config.yaml` 的 `model_name`） |

- 非 Development 環境沒有設定 `VibeMaker__LiteLlm__MasterKey` 時，API 會拒絕啟動。
- Development 沒設定時，會退回固定的 `VibeMaker__Pi__DevelopmentApiKey`（給 Fake LLM 用）。
- 想在本機不跑 LiteLLM 也驗證整個發 key 流程：用 `FAKE_LLM_MASTER_KEY=sk-dev dotnet run --project tests/Ymir.Testing.FakeLlm` 啟動 Fake LLM，它會模擬 `/key/generate`，並拒絕 master key 與未發放的 key。
