# ADR-0017：每次執行保存思考深度

- 狀態：已採納
- 日期：2026-10-07
- 依據：使用者要求由圖示開啟模型與思考深度設定；延伸 ADR-0003、0016。

對話、新對話與專案共用圖示設定面板，提供模型選擇及「自動／輕量／標準／深入」。保留模型名稱、文字與圖片能力徽章；已選模型與深度摘要放在圖示旁。面板支援鍵盤、點擊外部與 Escape 關閉，手機依可用 viewport 展開。

部署以 `VibeMaker:Models:*:SupportsThinking` 明確宣告支援 low、medium、high 的模型，預設 false，不根據模型名稱猜測。`GET /api/models` 加入 `supportsThinking`。目前三個 OpenRouter 模型經 [模型目錄](https://openrouter.ai/api/v1/models) 查證均接受這三種深度；MiniMax 維持模型預設，尚未開放深度調整。供應商是否顯示思考內容與選擇深度分開處理，沿用既有回覆顯示。

個人深度偏好存於 localStorage，未知值當成自動。送出時捕捉模型及深度，不支援的模型只傳 null；新對話第一則訊息同樣暫存該次選擇。後端拒絕無效深度及不支援的模型，依既有擁有者／模型開放檢查建立 execution，migration 新增 nullable `thinking_level`。null 與舊資料代表模型預設，排隊與重新啟動不重新讀取瀏覽器偏好，冪等重送保留原執行設定。

AgentRunRequest 交給 Pi harness，models.json 對支援模型宣告 `reasoning: true`、`compat.supportsReasoningEffort: true`。每次啟動明確傳 `--thinking low/medium/high`；自動傳 `--thinking off` 清除 Pi session 舊深度。已驗證 Pi 1.0.0 在此 OpenAI 相容設定下 off 不送出 `reasoning_effort`，代表供應商預設，不要求關閉模型原有思考。LiteLLM 再轉送深度至 OpenRouter（[官方 reasoning 契約](https://openrouter.ai/docs/guides/best-practices/reasoning-tokens)）。模型金鑰及 virtual key 授權沿用 ADR-0004／0016。

排隊執行啟動時再次確認模型與深度能力；能力被移除時明確失敗，不默默忽略。驗證使用 pinned Pi image 的本機假模型（`runtime/agent/test-thinking.mjs`）、LiteLLM 本機上游（`deploy/litellm/test-openrouter.py`）及瀏覽器 API fixtures，不呼叫付費模型。
