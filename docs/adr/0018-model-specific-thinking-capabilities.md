# ADR-0018：依模型與傳輸 API 提供思考選項

- 狀態：已採納
- 日期：2026-10-07
- 依據：使用者要求依各 API 的實際功能調整思考選單；取代 ADR-0017 的固定 low／medium／high 清單，其餘執行保存與授權規則沿用。

每個部署模型可配置 `Thinking:Parameter`、`Thinking:Levels`、`Thinking:DefaultLevel`、`Thinking:Required`。目前已驗證的傳輸為 `reasoning_effort` 與 OpenRouter 的 `reasoning.effort`；沒有能力設定的模型不顯示深度控制。舊 `SupportsThinking=true` 僅作 low／medium／high 相容回退，新部署使用明確能力資料。未實作的 token-budget／enable 開關 adapter 拒絕啟動，不將這些 API 冒充 effort。

API 回傳模型能力，前端按精確清單顯示；跨模型保存的偏好不在新模型清單時採模型預設，不強制映射。`null`／自動表示不送思考設定，`none` 是明確關閉，兩者不能互換。必須思考的模型不能宣告 none。後端在提交與執行開始兩次檢查精確值，execution 的原有 thinking_level 可容納 none／minimal／low／medium／high／xhigh／max，無資料庫變更。

2026-10-07 的 [OpenRouter 模型目錄](https://openrouter.ai/api/v1/models)顯示：GPT-6 Luna 支援 none／low／medium／high／xhigh／max，預設 medium；GPT-6.1 Sol 與 Sonnet 5.5 支援 low／medium／high／xhigh／max，必須思考，預設分別為 medium／high。MiniMax M2.7 不提供 effort 清單。模型能力由部署配置，更新時應重新核對目錄，不能依模型名稱或僅 reasoning=true 猜測。

目前 OpenRouter 經 LiteLLM 1.103.2 時，採 `reasoning.effort` 傳輸；其 reasoning_effort 轉換未原樣保留所有新值，已用本機假上游驗證。LiteLLM／OpenRouter 再轉成實際提供者接受的格式，不能套用其他路由的原生格式。參考 [OpenRouter reasoning 契約](https://openrouter.ai/docs/guides/best-practices/reasoning-tokens)。

Pi 1.0.0 需在 models.json 明確列出 xhigh／max 的 thinkingLevelMap，否則模型別名可能被降成 high。每次執行生成並校驗快取雜湊：明確 none 使用 off→none；自動時僅對本次模型關閉 Pi 的 effort 管理（reasoning=false），Pi CLI off 仍重設 session，但不向供應商傳關閉或深度參數。單純 off→null 會使 Pi 排除 off 並改用 minimal，不能用來實作自動。這些差異以 pinned Pi 映像與本機 fake server 驗證，未呼叫付費模型。
