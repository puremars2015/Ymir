# Ymir

始祖尤米爾，全系統的始祖，一個比 EIP 還要更完整的方案。

第一個子產品 **Vibe Maker**：企業使用者以公司帳號登入後，用對話的方式請 AI Agent 幫忙建立小工具與網站。
每個 Workspace 都有自己的隔離執行環境（Rootless Podman），Agent 的執行過程即時串流到瀏覽器。

```
Angular ──REST/SSE──> .NET 10 API ──> Runtime Manager ──podman exec──> Workspace Container（Pi Agent）──> LiteLLM ──> LLM
```

## 目前進度：Sprint 1（Walking Skeleton）✅

以 Dev 帳號登入 → 建立 Workspace / 對話 → 送出訊息 → Agent（Pi）在 Workspace 中執行並即時串流 → 歷史保存，已端對端打通。
進度與驗證紀錄見 [`docs/progress/`](docs/progress/)。

### Sprint 0（技術驗證 + 骨架）✅

- Pi Agent（RPC 模式）、Rootless Podman、LiteLLM 串接已實測通過，結果見 [`spikes/pi-rpc-poc/README.md`](spikes/pi-rpc-poc/README.md)
- .NET 10 模組化單體骨架、Angular 22 骨架、單元 / 整合測試、CI

下一步：Sprint 2 正式認證（OIDC + BFF），需要先確認企業 IdP 類型；路線圖見 [`docs/planning/development-plan.md`](docs/planning/development-plan.md)。

## 快速開始

需要 .NET 10 SDK、Node 24 LTS。

```bash
# 一鍵啟動 Fake LLM + API + Angular（不需要 container runtime）
dotnet run --project src/Ymir.AppHost

# 測試
dotnet test --solution Ymir.slnx
cd web && npm ci && npm test -- --watch=false
```

要讓 Agent 真的執行（而不是示範回應）：

```bash
npm install -g @earendil-works/pi-coding-agent@1.0.0
VibeMaker__Harness=Pi dotnet run --project src/Ymir.Api   # 搭配 dotnet run --project tests/Ymir.Testing.FakeLlm
```

## 文件

| 文件 | 說明 |
|---|---|
| [`docs/sa/vibe-maker-core-mvp-sa.md`](docs/sa/vibe-maker-core-mvp-sa.md) | 系統分析文件（原始檔：`documents/Vibe_Maker_Core_MVP_SA_v1.0.docx`） |
| [`docs/planning/development-plan.md`](docs/planning/development-plan.md) | 開發規劃、對 SA 的修訂建議、待確認事項 |
| [`docs/adr/`](docs/adr/) | 架構決策紀錄 |
| [`docs/progress/`](docs/progress/) | 開發進度留言版（每個 Sprint 一個看板） |
| [`runtime/agent/README.md`](runtime/agent/README.md) | Agent container image 與部署主機需求 |
| [`CLAUDE.md`](CLAUDE.md) | AI 開發規則（專案結構、指令、安全紅線） |
| `documents/` | 原始 SA 文件與架構圖 |
