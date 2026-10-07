#!/usr/bin/env bash
# 對 LiteLLM proxy 送一則訊息：./smoke-test.sh [model] [proxy-url]
#   model：minimax（預設）或 fake-model
# 查看某位 Ymir 使用者的預算與今天的用量：./smoke-test.sh usage <ymir-user-id> [proxy-url]
set -euo pipefail
cd "$(dirname "$0")"
set -a; . ./.env; set +a
if [ "${1:-}" = "usage" ]; then
  USER_ID="${2:?請指定 Ymir 使用者 id}"
  BASE="${3:-http://127.0.0.1:4000}"
  TODAY="$(date -u +%F)"
  curl -sS -m 30 "$BASE/user/info?user_id=$USER_ID" -H "Authorization: Bearer $LITELLM_MASTER_KEY"
  echo
  curl -sS -m 30 "$BASE/user/daily/activity?user_id=$USER_ID&start_date=$TODAY&end_date=$TODAY" -H "Authorization: Bearer $LITELLM_MASTER_KEY"
  echo
  exit 0
fi
MODEL="${1:-minimax}"
BASE="${2:-http://127.0.0.1:4000}"
curl -sS -m 120 "$BASE/v1/chat/completions" \
  -H "Authorization: Bearer $LITELLM_MASTER_KEY" \
  -H "Content-Type: application/json" \
  -d "{\"model\":\"$MODEL\",\"messages\":[{\"role\":\"user\",\"content\":\"用一句話回答：1+1 等於多少？\"}],\"max_tokens\":300}"
echo
