#!/usr/bin/env bash
# 官方 OAuth 登入在獨立互動容器完成；不向 proxy worker 塞登入提示。
set -Eeuo pipefail
umask 077
if [[ ${1:-} == --help ]]; then
    printf '使用：bash deploy/litellm/login-chatgpt.sh [--catalog-only]\n以目前 rootless Podman 帳號登入官方 ChatGPT；不呼叫模型推論。\n'
    exit 0
fi
[[ $EUID != 0 ]] || { printf '請使用目前的 itadmin 帳號執行，不要 sudo。\n' >&2; exit 1; }
[[ $# == 0 || ( $# == 1 && $1 == --catalog-only ) ]] || { printf '不支援參數。\n' >&2; exit 1; }
directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
command -v podman >/dev/null
volume=ymir-chatgpt-auth
podman volume exists "$volume" || podman volume create "$volume" >/dev/null
exec podman run --rm -it --name "ymir-chatgpt-login-$UID" \
    --cap-drop ALL --security-opt no-new-privileges --read-only \
    --tmpfs /tmp:rw,size=64m,mode=1777 \
    --env HOME=/tmp --env CHATGPT_TOKEN_DIR=/var/lib/litellm/chatgpt \
    --env CHATGPT_AUTH_FILE=auth.json \
    --volume "$volume:/var/lib/litellm/chatgpt" \
    --volume "$directory/chatgpt-oauth.py:/opt/ymir-chatgpt-oauth.py:ro" \
    --entrypoint python ghcr.io/berriai/litellm:v1.103.2 \
    /opt/ymir-chatgpt-oauth.py "$@"
