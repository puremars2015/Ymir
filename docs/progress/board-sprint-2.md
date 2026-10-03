# Sprint 2 · 正式認證

[← 回看板列表](README.md) ・ 規則與格式見 [README](README.md#留言規則)

---

## 📌 置頂：狀態總覽

> 最後更新：2026-10-03 23:13 ・ 狀態：**⏳ 尚未開始（等待決定）**

**目標**：以企業帳號登入（OIDC / Entra ID，經由 BFF，ADR-0002），完成 Admin / User 權限與帳號停用流程。

**Done Definition**：使用者以企業帳號登入、瀏覽器不持有任何 token；Admin 可以停用帳號，被停用的帳號立即無法使用 API 與建立 execution（SA 驗收條件 #1）。

| 工作項目 | 狀態 | 備註 |
|---|---|---|
| OIDC 登入（Authorization Code + PKCE，後端換 token、只發 cookie） | ⏳ | **需先確認 IdP 類型** |
| `IIdentityProvider`：IdP claims → `ExternalIdentity`（issuer + subject / oid） | ⏳ | 傳統 AD / LDAP 改用 adapter |
| 每個請求驗證使用者狀態（停用後既有 cookie 立即失效） | ⏳ | Sprint 1 只在建立 execution 時檢查 |
| Admin API：使用者列表、停用 / 啟用、角色設定（寫 audit） | ⏳ | SA §4 |
| Angular：企業帳號登入按鈕、Admin 使用者管理頁 | ⏳ | |
| 授權矩陣加入角色維度（User 不能呼叫 Admin API） | ⏳ | |
| 正式主機用完整 Containerfile 重跑 Podman 驗證 | ⏳ | 從 Sprint 1 移入；需要實際主機（見 [runtime/agent/README](../../runtime/agent/README.md#手動驗證)） |

**開工前要先有的決定**：企業 IdP 類型（Entra ID / ADFS / 純 LDAP）與測試用的 App 註冊資訊（client id、redirect URI）。

---

## 💬 留言區

### #001 · Sprint 2 看板開張

> 👤 **Claude（AI）** · 🕒 2026-10-03 23:13 · `❓待決定`

Sprint 1 已完成（[Sprint 1 看板](board-sprint-1.md)）。Sprint 2 的主要工作依賴企業 IdP 的資訊，請回覆：

1. IdP 類型：Entra ID（建議）/ ADFS / 純 LDAP？
2. 能否提供測試用的 App 註冊（client id、tenant、允許的 redirect URI，例如 `https://<host>/signin-oidc`）？
3. 有沒有可以實際跑 Podman 的 Linux 主機（cgroups v2）來完成 Podman 驗證？

不擋開工的部分（每個請求驗證使用者狀態、Admin API、授權矩陣角色維度）可以先做。

<details>
<summary>💬 回覆（0）</summary>

（尚無回覆）

</details>

---
