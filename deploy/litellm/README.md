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

## OpenRouter

LiteLLM 已設定下列三個 OpenRouter 路由，使用官方 `openrouter/` provider；對話、串流與工具呼叫仍經既有的 OpenAI 相容入口。OpenRouter 上游金鑰只放在 LiteLLM 的 `.env`，不交給瀏覽器、Ymir 使用者或 Agent。

| LiteLLM alias / Ymir Model ID | OpenRouter 模型 ID | 圖片輸入 / 工具 |
|---|---|---|
| `openrouter-sonnet-5.5` | `anthropic/claude-sonnet-5.5` | 支援 |
| `openrouter-gpt-6.1-sol` | `openai/gpt-6.1-sol` | 支援 |
| `openrouter-gpt-6-luna` | `openai/gpt-6-luna` | 支援 |

1. 在 `.env` 填入 `OPENROUTER_API_KEY`。模型設定使用 `openrouter/<OpenRouter 模型 ID>`，不要把 Ymir 的 alias 當成上游模型 ID。
2. `docker compose up -d --no-deps litellm` 重建 proxy，載入新的環境變數；保留既有 PostgreSQL volume 與金鑰。單純 `docker compose restart` 不會載入新的環境變數。
3. 將 [ymir-openrouter.env.example](ymir-openrouter.env.example) 的設定加入 Ymir API 與 RuntimeHost 部署環境，保留既有預設模型。`SupportsImages` 必須與所選模型相符；Agent 使用的模型需支援 `tools`。
4. 確認没有執行或排隊中的工作，再重啟 API／RuntimeHost。API 會重新建立模型清單並換發包含這三個 alias 的 virtual key；只改 LiteLLM 路由不會讓舊 key 自動取得新模型權限。
5. 使用者重新整理後即可從模型選單選取 OpenRouter。`./smoke-test.sh openrouter-sonnet-5.5` 可檢查上游文字回應；會使用 OpenRouter 帳號額度。

**新增其他 OpenRouter 模型**：在 `config.yaml` 加入不同的 `model_name`，例如 `openrouter-sonnet`，並直接設定 `model: openrouter/anthropic/claude-sonnet-4.6`、`api_key: os.environ/OPENROUTER_API_KEY`。在 Ymir 的 Models 與明確設定的 AllowedModels 同步加入相同 alias；使用者即可逐一選用。不要使用無限制的 wildcard key。

未設定金鑰時，OpenRouter 路由無法呼叫；既有 MiniMax 路由仍可使用。不要先把未設定金鑰的路由開放到模型選單。

用量與預算沿用 ADR-0011。費用依 LiteLLM 的 OpenRouter 模型價格資料與上游回報；新增模型後需確認用量頁的非零費用，不能把費用強制設為 0。

官方說明：[LiteLLM OpenRouter provider](https://docs.litellm.ai/docs/providers/openrouter)、[OpenRouter 模型清單](https://openrouter.ai/docs/api/api-reference/models/get-models)。這次接入服務於既有 Agent 的聊天、圖片輸入與工具呼叫；Ymir 尚未加入圖片生成、語音或 embeddings 的使用者入口。

## MiniMax 與共用設定注意

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
| `VibeMaker__LiteLlm__AllowedModels__0` | key 可用的模型；未設定時允許 `VibeMaker__Models` 清單，沒有模型清單時只有預設模型 |
| `VibeMaker__LiteLlm__KeyLifetime` / `RenewBefore` | 有效期（預設 `1.00:00:00`）、提前換發時間（預設 `01:00:00`） |
| `VibeMaker__LiteLlm__MonthlyBudgetUsd` | 每人每月模型預算（美元，30 天一期；0 或不設表示不限制）。管理介面「系統設定 → 執行環境」的值優先 |
| `VibeMaker__LiteLlm__MaxBudget` | 舊設定：每把 key 的預算。key 每 24 小時換發，等於每天重置，建議改用 `MonthlyBudgetUsd` |
| `VibeMaker__Pi__ModelBaseUrl` | **Agent container 內**連 LiteLLM 的位址：`http://host.containers.internal:4000/v1`（Podman）或 `http://host.docker.internal:4000/v1`（Docker）；要讓管理員可以關閉 Agent 的對外連線時，改成接在 Agent network 上的 `http://litellm:4000/v1`（見 `deploy/runtime-host/README.md`「受限網路」） |
| `VibeMaker__Pi__ModelId` | 預設模型，例如 `minimax`（對應 `config.yaml` 的 `model_name`） |
| `VibeMaker__Models__0__Id` / `VibeMaker__Models__0__DisplayName` | 對話中可選的模型清單（`__1__`、`__2__` 依序增加）；Id 必須與 `config.yaml` 的 `model_name` 相同。未設定時只有預設模型 |

- **新增模型**：先在 `config.yaml` 加一筆 `model_name`，再在 Ymir 的 `VibeMaker__Models__*` 加同名的 Id。
  - virtual key 預設只允許清單內的模型；
  - 選了清單外的模型，API 回 `400 MODEL_NOT_AVAILABLE`。
- 非 Development 環境沒有設定 `VibeMaker__LiteLlm__MasterKey` 時，API 會拒絕啟動。
- Development 沒設定時，會退回固定的 `VibeMaker__Pi__DevelopmentApiKey`（給 Fake LLM 用）。
- 想在本機不跑 LiteLLM 也驗證整個發 key 流程：用 `FAKE_LLM_MASTER_KEY=sk-dev dotnet run --project tests/Ymir.Testing.FakeLlm` 啟動 Fake LLM。
  - 它會模擬 `/key/generate`，並拒絕 master key 與未發放的 key；
  - 也會模擬使用者、預算與用量（`/user/*`、`/user/daily/activity`）：每次呼叫算 10 + 5 個 token、US$0.01。

## 用量與每人預算（ADR-0011）

- **key 掛在使用者底下**：Ymir 發 key 時帶 `user_id`（= Ymir 使用者 id）。發 key 前先以 `/user/update`（不存在時 `/user/new`）建立或更新 LiteLLM 使用者，並設定 `max_budget` 與 `budget_duration: 30d`。
  - LiteLLM 因此依使用者彙總花費，並強制每人每月預算：用完後該使用者的 key 都無法呼叫模型。
  - Ymir 送訊息前也會先檢查，預算用完時直接告訴使用者。
  - 這個功能上線前發出的 key 沒有 `user_id`，它們的花費不會算到使用者身上；這些 key 最晚 24 小時後換發。
- **管理介面「用量」**：每位使用者的費用、輸入 / 輸出 token、請求數、本期已用 / 預算。
  - 資料來自 `GET /user/daily/activity?user_id=&start_date=&end_date=` 與 `GET /user/info?user_id=`。
- **單價**：費用依 `config.yaml` 每個模型的 `input_cost_per_token` / `output_cost_per_token` 計算。
  - LiteLLM 沒有 MiniMax 的內建單價：沒填時**費用為 0、預算永遠不會用完**，但 token 數仍然正確。
  - 請依 MiniMax 方案填入（單位：美元 / token，例如每百萬 token US$0.30 → `0.0000003`）。
- **手動驗證**（使用者環境，沙箱沒有真正的 LiteLLM）：
  1. 在管理介面設定每月預算後，以一般使用者送一則訊息；
  2. 執行 `./smoke-test.sh usage <Ymir 使用者 id>`，應看到該使用者的 `max_budget` 與當天的 token / 花費；
  3. 把預算設得很低（例如 0.01），送幾則訊息後應被擋下，並顯示「本月模型預算已用完」。

### 不使用真實金鑰的路由驗證

在本目錄執行（容器名稱依部署調整）：

```sh
docker cp config.yaml ymir-litellm-litellm-1:/tmp/ymir-openrouter-config-test.yaml
docker cp test-openrouter.py ymir-litellm-litellm-1:/tmp/ymir-test-openrouter.py
docker exec ymir-litellm-litellm-1 python /tmp/ymir-test-openrouter.py /tmp/ymir-openrouter-config-test.yaml
```

測試在程序內啟動 HTTP stub，驗證三個 alias、上游模型 ID、文字回應、工具呼叫與串流；不使用真實金鑰、不呼叫外部模型、不寫入用量資料庫。
