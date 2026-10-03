# ADR-0001：模組化單體與命名（Ymir / Vibe Maker）

- 狀態：已採納
- 日期：2026-10-02

## 背景

Repo 名稱是 **Ymir**（README：「全系統的始祖，比 EIP 更完整的方案」），SA 的產品是 **Vibe Maker**，命名空間建議為 `VibeMaker.*`，分成 Api / Application / Domain / Infrastructure / Contracts 五個專案（SA §19）。

Ymir 未來會承載 Vibe Maker 以外的子系統（EIP、MES 等整合），身份、使用者、稽核等能力應該共用。

## 決策

1. **Ymir 是母平台，Vibe Maker 是第一個子產品模組。**
2. 採**模組化單體（Modular Monolith）**，單一部署單元、單一 API Host：

   ```
   src/
     Ymir.Api                                  Host / composition root
     Ymir.AppHost, Ymir.ServiceDefaults        .NET Aspire 開發編排、OTel 預設
     Platform/Ymir.Platform                    共用核心：使用者、身份、稽核（Domain + Application）
     Platform/Ymir.Platform.Infrastructure     SQL Server、OIDC 等實作
     Modules/VibeMaker/Ymir.VibeMaker          Workspace / Conversation / Agent / Runtime（Domain + Application）
     Modules/VibeMaker/Ymir.VibeMaker.Infrastructure   Podman / Pi / LiteLLM adapters
     Modules/VibeMaker/Ymir.VibeMaker.Contracts        API DTO 與 SSE 事件契約
   ```

3. 每個模組的 Domain 與 Application 放在同一個專案，以資料夾分層（`Domain/`、`Application/`）；Infrastructure 獨立專案。**專案參考方向**保證 Application 不會直接依賴 Podman CLI 或 Pi 協定（SA §19 的要求）。
4. 依賴方向：`Api → *.Infrastructure → 模組核心 → Contracts`；模組之間不得互相參考，只能透過 `Ymir.Platform` 的公開抽象。
5. 每個模組擁有自己的 DB schema（`platform.*`、`vibemaker.*`）與 DbContext，跨模組只以 ID 參照。

## 影響

- 專案數比 SA 的 5 個略多，但新增子系統時只要新增 `Modules/<Name>/`，不需改動共用核心。
- 未來要把 Runtime Manager 拆成獨立服務（SA §16）時，只需把 `Ymir.VibeMaker.Infrastructure` 的 Podman 部分搬到新 Host，並以 HTTP/gRPC 實作同一個 `IAgentRuntimeManager`。
