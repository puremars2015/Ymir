# ADR-0031：以 LLM 自動路由選擇回覆模型（Auto）

- 狀態：已採納（待實作；「待確認」各項需在實作第一階段驗證）
- 日期：2026-10-19
- 依據：使用者要求由一個專門做「判斷」的模型讀對話內容，決定這一輪用輕、中、重哪一級的模型，並且**預設就用 LLM auto router**。延伸 [ADR-0020](0020-admin-controlled-model-access.md)（管理員控制開放模型）、[ADR-0019](0019-user-model-access.md)（個人模型權限）、[ADR-0004](0004-litellm-virtual-keys.md)（模型金鑰）。

## 背景

- 現在每次送訊息都由使用者選模型（或沿用對話上次的選擇、再不然用預設），在 `ExecutionService` 解析後固定寫進 `AgentExecution.ModelId`；`ExecutionRunner` 啟動前以個人生效清單（ADR-0019）重新檢查，`PiAgentHarness` 以 `--model` 傳給 Pi。
- 每次回覆都是全新的 Pi 程序（ADR-0003），所以**每一輪換模型不需要改 Pi**，也不會破壞 session。
- 使用者要求依對話內容自動分級，不必每次自己判斷該用哪個模型。已決定：
  - 路由器：OpenRouter 的 `typesafe/jev-1.14`（專門做判斷的新型模型）。
  - 分級：`gpt-6-luna` = 輕、`gpt-6.1-sol` = 中、`gpt-6-astra` = 重。
  - 新對話預設使用自動路由。

## 決策

### 1. 「自動」是虛擬模型 `auto`

- `auto` 不是 LiteLLM 的模型，也不是 Pi 的模型；它只存在於 Ymir 的模型清單，由 `ExecutionRunner` 在啟動 Pi 前換成一個具體模型。
- `GET /api/models` 在路由可用時把 `auto`（顯示名稱「自動」）放在清單最前面並標記 `isAuto`；使用者仍可手動選任何固定模型，手動選擇的行為完全不變。
- **預設**：路由啟用且對該使用者可用時，`auto` 是預設模型（新對話、未曾選過模型的對話）。已經明確選過固定模型的對話維持原選擇。管理員可把系統預設改回固定模型（`vibemaker.model_access` 的 `defaultModelId` 允許 `auto`）。
- 對話的 `ModelId` 記錄使用者的選擇（可為 `auto`），所以下一則訊息會重新路由；實際用的模型記在 execution。

### 2. 分級對應與設定

- 部署設定 `VibeMaker:Router`：`Enabled`、`Model`（LiteLLM alias，例如 `openrouter-jev-1.14`，只能來自部署設定）、`Timeout`（預設 3 秒）、`Tiers:Light|Standard|Heavy`（各含 `ModelId` 與選用的 `ThinkingLevel`）。每一級的模型必須在 `VibeMaker:Models` 中，與 LiteLLM `AllowedModels` 取交集（沿用 ADR-0020 的上限規則）。
- 預設對應：輕 = `gpt-6-luna`、中 = `gpt-6.1-sol`、重 = `gpt-6-astra`；模型 ID 以部署的 LiteLLM alias 為準（OpenRouter 上目前有 luna 與 sol 的 alias；astra 目前只在 ChatGPT OAuth 路由，見下方待確認）。
- 管理員在「管理 → 系統設定 → 模型」只能：開關自動路由、在已部署的模型中調整三級的對應與思考深度、設定系統預設是否為 `auto`。管理員**不能**新增任意路由器模型 ID 或金鑰（與 ADR-0020 一致）。設定存 `platform.system_settings` 的 `vibemaker.model_router`，未儲存時沿用部署設定，可還原；變更與還原寫稽核。
- 思考深度：選 `auto` 時，思考深度由該級的 `ThinkingLevel` 決定，前端隱藏深度選擇；手動選固定模型時照舊由使用者選。

### 3. 路由發生在 `ExecutionRunner`，並與 runtime 準備並行

- 流程：模型權限檢查之後、`EnsureRuntime` 之前就啟動路由工作，等 runtime 與設定檔準備完成、要啟動 Pi 前才 `await` 結果。準備工作本來就要數秒，路由的延遲大多被蓋住，**不增加送出時的等待**，也不拖慢送出的 HTTP 請求。
- 路由結果寫回 `AgentExecution`：`ModelId` 改為實際用的模型，新增 `RequestedModelId`（`auto`）、`RoutedTier`、`RoutingSource`（`rule` / `llm` / `fallback`）、`RoutingMillis`（需要 migration）。完成後送一個狀態事件，例如「已自動選用 GPT-6.1 Sol」，對話訊息旁顯示實際用的模型。
- `ExecutionService` 的 `AcceptsThinking` 檢查在 `auto` 時略過，改在路由後於 runner 以實際模型檢查；權限檢查（ADR-0019 的個人生效清單）一律對實際模型做。
- 群組 `@bot`（ADR-0026）使用呼叫者的設定，若呼叫者選 `auto`，同樣由路由決定，輸入只有群組指示與最近的群組訊息背景。

### 4. 先規則、後 LLM；LLM 失敗一律降級

1. **規則層**（不呼叫 LLM，零額外成本與延遲）：
   - 很短的招呼或單句、沒有附件、此對話沒有工具使用紀錄 → 輕。
   - `/make`、「小工具架設」主題或有附件 → 下限為中（LLM 仍可升到重）。
2. **LLM 層**：其餘情況呼叫 `typesafe/jev-1.14`，要求回傳固定格式 `{"tier":"light|standard|heavy"}`（以 JSON schema / 結構化輸出限制，`max_tokens` 很小，另可回短的 `reason` 只寫 server log，不回給瀏覽器）。
3. **降級**：逾時、格式錯誤、路由器不可用、回傳不在三級內 → 一律取「中」，execution 照常，`RoutingSource = fallback`。連續失敗達門檻（預設 1 分鐘內 5 次）時熔斷 60 秒，期間直接取「中」，避免路由器故障讓每則訊息多等一個逾時。
4. **等級不可用時**：若使用者的個人生效清單沒有該級的模型，往最接近的等級退（重 → 中 → 輕，輕 → 中）；三級都不可用時，`auto` 對該使用者不出現，指定 `auto` 回 `MODEL_NOT_AVAILABLE`。

### 5. 路由器的輸入與穩定性

- 輸入：這一則訊息（截斷）、最近 3～4 輪的簡短內容（每則截斷）、訊號（附件數與類型、`/make` 主題、對話是否曾使用工具或產生檔案、訊息長度、上一輪的等級）。不送整段對話、檔案內容、系統 prompt、金鑰或任何憑證。
- 內容放在明確標示的資料區塊，路由器的指示在區塊之外；路由器的輸出只當作不可信的列舉值解析，其餘文字一律丟棄。
- **避免來回切換**：同一對話升級立即生效；降級要連續 2 次判為較低等級才生效（以最近兩個 execution 的 `RoutedTier` 判斷，不另存狀態），以免提示快取一再失效、回答風格跳動。

### 6. 路由器的金鑰與費用

- 路由呼叫使用**專用的路由 virtual key**：由 API 經 `IModelGateway` 發給路由器模型（`allowedModels` 只含路由器模型、有預算上限），只存在 API 程序，**不進 Agent container**（延續 ADR-0004）。路由器模型不加進使用者的 virtual key，也不出現在使用者模型清單。
- 呼叫帶 `user` 欄位（使用者 id），讓 LiteLLM 記到該使用者名下，便於歸屬；是否計入個人月預算與用量頁顯示，列為待確認（路由費用預期遠小於回覆）。
- 主回覆仍用使用者自己的 virtual key，key 的允許模型清單照 ADR-0019／0020 是個人生效清單（含三級模型）。

### 7. 安全與隱私

- 路由結果只能在使用者**本來就有權限**的模型中選；使用者在訊息裡寫「請用最貴的模型」只能影響等級選擇，不能越權，也不繞過預算與配額。
- 路由器看到的是使用者對話內容（已截斷），經 LiteLLM 送到 OpenRouter 上的第三方模型，與既有 OpenRouter 模型走同一條資料路徑；上線前需確認 `typesafe` 的資料處理條款可接受（待確認）。
- 路由 prompt 與使用者內容不寫稽核；稽核維持既有的 execution 事件。server log 只記等級、來源、耗時與（截斷的）reason，不記對話內容。

### 8. 可觀測性

- 指標（`VibeMakerTelemetry`）：`ymir.model_router.decisions`（tier、source）、`ymir.model_router.duration`、`ymir.model_router.failures`、熔斷次數。
- execution 紀錄保留等級與來源，供管理員之後在用量頁看分布、評估判斷準確度與成本。

## 影響

- 新增：`IModelRouter`（Application）與 LiteLLM/OpenRouter 實作（Infrastructure）、`vibemaker.model_router` 設定與管理端點、`agent_executions` 的路由欄位（migration）、`/api/models` 的 `isAuto`（OpenAPI 快照與前端型別重新產生）、前端模型選擇器的「自動」與實際模型顯示。新管理端點加入授權矩陣（Admin + XSRF）。
- 修改：`ExecutionService`（接受 `auto`、略過思考深度檢查）、`ExecutionRunner`（並行路由、寫回實際模型、狀態事件）、`ModelCatalog` / 模型存取（`defaultModelId` 允許 `auto`）。Pi、runtime host、container 與 SSE 契約的事件型別不變（只多一個 `status` 文字）。
- 測試：路由結果解析與降級、熔斷、等級退讓、降級需連續兩次、個人權限不越權的單元測試；Fake LLM 新增可回應路由 prompt 的腳本（例如訊息帶 `[tier:heavy]`）供整合測試；Playwright 驗證預設為自動、顯示實際模型、路由器逾時時退回中。
- 風險：路由器判斷不準會造成成本或品質偏差（以 §8 的紀錄調校規則與 prompt）；多一次外部呼叫（以並行與熔斷控制）；astra 的供應來源可能受共用額度限制（見下）。

## 待確認

1. **`typesafe/jev-1.14` 在 OpenRouter 的實際模型 ID、是否支援結構化輸出（JSON schema）、延遲與價格**：第一階段先以實測決定 `Timeout` 與是否需要規則層更積極地省略 LLM 呼叫；若不支援結構化輸出，改用嚴格解析與重試一次。
2. **`gpt-6-astra` 的供應來源**：目前 LiteLLM 只有 ChatGPT OAuth 的 `chatgpt-gpt-6-astra`（共用帳號、有額度限制，見 `deploy/litellm/CHATGPT-OAUTH.md`），OpenRouter 沒有對應 alias；重等級要依賴它，需確認額度足夠，或另補 OpenRouter 路由。
3. **路由費用是否計入個人月預算**：預設只做歸屬、不計入預算檢查；若要計入需調整 `IModelGateway` 的用量彙整。
4. **`typesafe` 的資料處理條款**是否符合公司規範（§7）。

## 實作順序

1. P1：部署設定與管理設定、`auto` 虛擬模型、規則層、降級預設（中）、execution 路由欄位與 migration、前端顯示；此時 LLM 層可先關閉。
2. P2：接上 `typesafe/jev-1.14`、專用路由 key、與 runtime 準備並行、熔斷、指標、降級需連續兩次。
3. P3：依紀錄調整規則與路由 prompt，評估是否在用量頁顯示分布。
