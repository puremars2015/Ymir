#!/bin/bash
# Stop hook：每次修改後都必須 commit、更新 docs/progress/ 進度留言版、push（CLAUDE.md「每次修改後的流程」）。
# 任一項未完成就回傳 decision=block，讓 Claude 繼續把事情做完。
# 只使用 git 唯讀指令；commit 訊息與進度留言需要 AI 撰寫，hook 不代做。
set -uo pipefail

input="$(cat)"
stop_hook_active="$(printf '%s' "$input" | jq -r '.stop_hook_active // false' 2>/dev/null || echo false)"

cd "${CLAUDE_PROJECT_DIR:-$(dirname "$0")/../..}" || exit 0
git rev-parse --is-inside-work-tree >/dev/null 2>&1 || exit 0

block() {
  jq -n --arg reason "$1" '{decision: "block", reason: $reason}'
  exit 0
}

branch="$(git rev-parse --abbrev-ref HEAD)"

# 1. 未 commit 的變更
if [ -n "$(git status --porcelain)" ]; then
  block "工作目錄有未 commit 的變更。請依 CLAUDE.md「每次修改後的流程」：確認 build/test/lint 通過後 commit，接著在 docs/progress/ 目前 Sprint 看板留言並更新置頂區、commit，最後 push 到 origin/${branch}。"
fi

# 2. 進度留言版是否跟上：最後一次修改 docs/progress/ 之後，不應再有修改其他檔案的 commit
last_progress_commit="$(git log -1 --format=%H -- docs/progress/ 2>/dev/null)"
if [ -n "$last_progress_commit" ]; then
  if [ -n "$(git diff --name-only "$last_progress_commit" HEAD -- . ':(exclude)docs/progress/' 2>/dev/null)" ]; then
    block "最後一次更新 docs/progress/ 之後還有其他 commit，進度留言版沒有跟上。請在目前 Sprint 的看板留言（做了什麼、實際跑過的驗證、卡關與待決定事項）並更新置頂區，commit 後 push 到 origin/${branch}。"
  fi
fi

# 3. 未 push 的 commit
if git rev-parse --abbrev-ref --symbolic-full-name '@{u}' >/dev/null 2>&1; then
  unpushed="$(git rev-list --count '@{u}..HEAD' 2>/dev/null || echo 0)"
else
  unpushed="no-upstream"
fi

if [ "$unpushed" != "0" ]; then
  message="有尚未 push 的 commit。請執行 git push -u origin ${branch}（網路失敗依 2/4/8/16 秒重試）。"
  if [ "$stop_hook_active" = "true" ]; then
    # 已經因 hook 繼續過一次：只提醒，避免 push 因網路問題失敗時無限循環。
    jq -n --arg msg "提醒：$message" '{systemMessage: $msg}'
    exit 0
  fi
  block "$message"
fi

exit 0
