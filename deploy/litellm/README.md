# LiteLLM sample（MiniMax 國際站）

Ymir 的模型入口是 LiteLLM（SA §19、[ADR-0004](../../docs/adr/0004-litellm-virtual-keys.md)）。這個 sample 用 **MiniMax 國際站（minimax.io）訂閱帳號**當上游，
另外附一個轉給 Fake LLM 的 `fake-model`，方便離線驗證。

| 檔案 | 用途 |
|---|---|
| `config.yaml` | LiteLLM 設定：`minimax`（`https://api.minimax.io/v1`，OpenAI 相容）、`fake-model` |
| `compose.yml` | 以 container 執行 LiteLLM proxy（只綁 `127.0.0.1:4000`） |
| `.env.example` | 金鑰範本；複製成 `.env` 後填入（**`.env` 已被 `.gitignore` 排除，不得提交**） |
| `smoke-test.sh` | 對 proxy 送一則訊息 |

## 使用

```bash
cd deploy/litellm
cp .env.example .env        # 填入 MINIMAX_API_KEY，並自行產生 LITELLM_MASTER_KEY
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
- **Ymir API 接 LiteLLM**：設定 `VibeMaker__Pi__ModelBaseUrl`（container 內用 `http://host.containers.internal:4000/v1` 或 `http://host.docker.internal:4000/v1`）與 `VibeMaker__Pi__ModelId=minimax`。目前正式的金鑰發放流程（virtual key）尚未實作，開發期可以先用 `DevelopmentApiKey`。
