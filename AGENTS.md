# 專案開發規則

完整架構、驗證與交付規則見 [CLAUDE.md](CLAUDE.md)。所有開發代理均需遵守，並在工作前閱讀 `docs/progress/` 的目前 Sprint 看板。

## Git（使用者決定：2026-10-12）

- 直接在 `main` 工作；除非使用者明確要求，不建立工作分支、額外 worktree 或 PR。
- 工作前及 push 前，於乾淨工作目錄執行 `git fetch origin`、`git rebase origin/main`，將未推送的本機提交接在最新遠端歷史之後。
- 保留既有未提交變更與其他人的提交；解決 rebase 衝突後重新驗證，再 `git rebase --continue`。
- 完成相關驗證、commit 與 `docs/progress/` 紀錄後，用一般 `git push origin main` 發布；禁止 force push 與重寫已推送歷史。
- 追蹤 `main` 的 CI；失敗需修正並重新驗證，不跳過或停用測試。
