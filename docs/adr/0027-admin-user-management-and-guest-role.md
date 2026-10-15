# ADR-0027：管理員的使用者管理（改名、角色、刪除）與訪客角色

- 狀態：已採納（使用者 2026-10-15 要求實作）
- 日期：2026-10-15
- 修訂：ADR-0009（角色多了訪客；Admin 可以變更本機帳號的角色）、ADR-0023 / ADR-0026（訪客沒有訊息與群組）、ADR-0024（Google 的 AdminEmails 只同步「是否為 Admin」）

## 背景

使用者要求修改「管理 → 使用者」：

1. 使用者名稱可以設定。
2. 可以刪除使用者帳號。
3. 可以變更帳號的權限等級。
4. 新增角色「訪客」：與使用者相同，但不能使用聊天系統（使用者之間的訊息，ADR-0023），也沒有群組系統（ADR-0026）。

限制：

- 企業帳號（Entra app role）與 Google 帳號（`AdminEmails`）每次登入都會同步角色（ADR-0009、ADR-0024）；如果 Ymir 直接改角色，下次登入就會被覆寫。
- `platform.direct_messages`、`chat_group_*` 以 FK 指向 `platform.users`（不串聯刪除），Vibe Maker 的資料、稽核、用量也以 user id 參照（ADR-0001）；實際刪除資料列會失敗或讓歷史紀錄找不到名稱。
- 每次登入都以 IdP 的資料更新顯示名稱（SA §4.1）。

## 決策

### 1. 顯示名稱

- `PUT /api/admin/users/{id}/display-name`（1～200 字，前後空白去除），稽核 `admin.user.rename`。
- `platform.users.display_name_overridden`：Admin 設定過之後，登入不再以 IdP 的名稱覆寫（帳號、email 仍同步）。
- 既有 cookie 在下一個請求換發（`CookiePrincipalValidator` 比對名稱）。

### 2. 角色與訪客

- `UserRole.Guest`（資料庫存 `GUEST`，欄位已是字串，不需要 migration）。
- `PUT /api/admin/users/{id}/role`，稽核 `admin.user.role.<role>`；下一個請求起生效（角色與 cookie 不同就換發）。
- 不能變更自己的角色（避免管理員把自己降級後失去管理介面）。
- **IdP 只管理「是否為 Admin」**：`User.SyncRole` 在 IdP 給 Admin 時升級、拿掉 Admin 時降為使用者；非 Admin 之間（使用者 / 訪客）由 Ymir 管理，IdP 不會把訪客改回使用者。
  因此企業帳號與 Google 帳號在 Ymir 只能在使用者與訪客之間切換，授予或拿掉 Admin 回 400 `ROLE_MANAGED_BY_IDP`（仍以 Entra app role / `AdminEmails` 設定）。
  `AdminUserResponse.CanChangeAdminRole` 告訴前端能否在這裡授予或拿掉 Admin。本機帳號與開發登入可以設定任何角色。
- 新登入的 SSO 使用者仍依 IdP 給 Admin 或使用者；要讓某人以訪客身分使用，Admin 在第一次登入後改成訪客。
- 建立本機帳號時可以直接選擇訪客。

### 3. 訪客的限制

- 授權 policy `AuthSetup.MessagingPolicy`（角色 User 或 Admin）套在 `/api/direct-messages/*` 與 `/api/chat-groups/*`：訪客一律 403，包含 SSE；
  已連線的 SSE 在心跳時發現改成訪客就結束。
- 訪客不出現在其他人的聯絡人名單，不能收到一對一訊息（回 404，與不存在相同），不能被加入群組（400）。
- 其他功能（Agent 對話、專案、檔案、知識庫、網站、OneDrive、模型、擴充能力）與使用者相同。
- 已在群組中的使用者被改成訪客時，成員資格保留但無法再存取；改回使用者後恢復。
- 前端 `canUseMessaging`：訪客不顯示右側成員面板與按鈕、不連 `/api/direct-messages/events`。

### 4. 刪除帳號

- `DELETE /api/admin/users/{id}`，稽核 `admin.user.delete`；不能刪除自己。
- 刪除是**標示刪除**（`UserStatus.Deleted`）：資料列保留給訊息、稽核、用量等歷史紀錄顯示名稱；email 清除。
- 外部身分釋放：`(issuer, subject)` 改為 `urn:ymir:deleted` + user id，本機帳號的密碼一併刪除。
  同一個企業帳號 / Google 帳號 / 本機帳號名稱之後可以重新建立，成為**新的使用者**（不會接回舊資料）。要阻止某人登入請用「停用」。
- 刪除後與停用相同：下一個請求 cookie 失效，`UserDeactivationService` 取消執行中的工作、撤銷模型 key、停止 runtime；對話、專案與 workspace 檔案保留（與封存的原則一致）。
- 已刪除的帳號不出現在管理清單、總覽統計、聯絡人名單；停用 / 啟用 / 改名 / 改角色 / 重設密碼都回 404。

## 影響

- Platform migration `UserAdminManagement`（`display_name_overridden` 欄位，預設 false）。
- 授權矩陣加入三個 Admin 端點；`CookiePrincipalValidator` 與 `ExecutionService` 改為「不是 Active 就拒絕」。
- 已刪除使用者擁有的群組保留，成員仍可對話，但沒有擁有者可以管理；之後如需要再提供轉移擁有者。
