# 專案開發規則

完整架構、驗證與交付規則見 [CLAUDE.md](CLAUDE.md)。所有開發代理均需遵守，並在工作前閱讀 `docs/progress/` 的目前 Sprint 看板。

## 處理紀錄

| 日期 | 內容 | 備註 |
|---|---|---|
| 2026-10-15 16:15 CST | 同步 main 並經本機 sudo 授權完成 3855dd6 正式部署；API image 57c69ad7f1727f5001883469a495a32d4642a371b654bc49f834205fc99be932，RuntimeHost /opt/ymir/releases/3855dd6/runtime-host。ChatGroups 與 OneDriveLinkedItems migration 已套用，四個正式 unit active、API health 與公開登入頁 HTTP 200。 | 最終備份 /var/backups/ymir/3855dd6-retry3；先前嘗試的備份保留。修正部署器的 UID/GID 比對、SQL SET options、掛載排序及 Tunnel 就緒重試；13 部署檢查及隔離 SQL 雙次套用通過。真登入／加密資料／Pi SSE／Graph 待人工驗收，OneDrive 雲端寫回仍未啟用。 |
| 2026-10-14 | 本輪範圍固定為已取得的7個分支，加入DM1成員面板／一對一訊息及Google SSO；Google ADR編號碰撞改為0024，保留DM的0023與兩種e2e命令。最終後端build無警告、533單元／294整合測試全通過、無略過；前端lint／186測試／build通過。 | Google尚未配置正式憑證、不主動啟用；正式環境仍4b8a3af；待建立新API／RuntimeHost產物、DirectMessages＋RAG migration與單次root切換。 |
| 2026-10-14 | 將目前 5 個其他分支的提交整合／驗證包含於 main；新增 RAG 引導主題 migration，保留 OneDrive ADR 提議與每模型用量功能。修正 OIDC 整合測試的共用 fixture 狀態殘留；後端 build 無警告、523 單元測試、275 整合測試全通過；前端 lint、178 測試與 build 通過。 | RAG merge 9f363c4；SQL 測試使用獨立暫時容器，測完清理；正式環境仍4b8a3af，重新部署尚待新版產物／migration 審查及單次 sudo 授權。原有本機變更與 stash 保留。 |
| 2026-10-14 13:00 CST | 經 Telegram 單次 sudo 授權完成 4b8a3af 正式更新：API image 948fdadbc6196548465fb4e3373d384e22dd710af45caa340954fb91c0d8485f，RuntimeHost /opt/ymir/releases/4b8a3af/runtime-host。root 更新器確認 SQL/keyring/workspaces 備份、migration history/schema gate、有效 ENV、精確 image、健康與開機設定；後續讀回四個正式 unit active、公開 /login HTTP 200、Quadlet image 與 RuntimeHost 工作目錄一致。 | 備份 /var/backups/ymir/4b8a3af；未回復；單次表單及臨時 tunnel 已清理。真 Entra 登入、既有加密資料解密與真 Pi SSE 尚待人工驗收。保留既有未提交工作樹變更，未宣稱整站全部功能驗收完成。 |

## Git（使用者決定：2026-10-12）

- 直接在 `main` 工作；除非使用者明確要求，不建立工作分支、額外 worktree 或 PR。
- 工作前及 push 前，於乾淨工作目錄執行 `git fetch origin`、`git rebase origin/main`，將未推送的本機提交接在最新遠端歷史之後。
- 保留既有未提交變更與其他人的提交；解決 rebase 衝突後重新驗證，再 `git rebase --continue`。
- 完成相關驗證、commit 與 `docs/progress/` 紀錄後，用一般 `git push origin main` 發布；禁止 force push 與重寫已推送歷史。
- 追蹤 `main` 的 CI；失敗需修正並重新驗證，不跳過或停用測試。
