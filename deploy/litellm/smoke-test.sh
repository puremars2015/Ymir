#!/usr/bin/env bash
# 對 LiteLLM proxy 送一則訊息：./smoke-test.sh [model] [proxy-url]
#   model：minimax（預設）或 fake-model
set -euo pipefail
cd "$(dirname "$0")"
set -a; . ./.env; set +a
MODEL="${1:-minimax}"
BASE="${2:-http://127.0.0.1:4000}"
curl -sS -m 120 "$BASE/v1/chat/completions" \
  -H "Authorization: Bearer $LITELLM_MASTER_KEY" \
  -H "Content-Type: application/json" \
  -d "{\"model\":\"$MODEL\",\"messages\":[{\"role\":\"user\",\"content\":\"用一句話回答：1+1 等於多少？\"}],\"max_tokens\":300}"
echo
