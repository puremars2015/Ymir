#!/usr/bin/env bash
# 手動建立一個 workspace runtime，參數與 PodmanRuntimeManager（PodmanCommandBuilder）相同。
# 用法：./run-example.sh <workspace-root> [image]
# 必須以「一般使用者」執行（rootless），不可用 root / sudo。
set -euo pipefail

WORKSPACE_ROOT="${1:?usage: $0 <workspace-root> [image]}"
IMAGE="${2:-localhost/ymir/agent-runtime:dev}"
WORKSPACE_ID="$(cat /proc/sys/kernel/random/uuid | tr -d '-')"
NAME="ymir-ws-${WORKSPACE_ID}"

mkdir -p "${WORKSPACE_ROOT}/${WORKSPACE_ID}/workspace" "${WORKSPACE_ROOT}/${WORKSPACE_ID}/agent-state"

podman run --detach --name "${NAME}" \
  --hostname ymir-runtime \
  --init \
  --userns keep-id:uid=1000,gid=1000 \
  --cap-drop ALL \
  --security-opt no-new-privileges \
  --read-only \
  --tmpfs /tmp:rw,size=512m \
  --tmpfs /home/agent:rw,size=256m \
  --pids-limit 512 \
  --memory 2g \
  --cpus 1.0 \
  --network slirp4netns \
  --volume "${WORKSPACE_ROOT}/${WORKSPACE_ID}/workspace:/workspace:rw" \
  --volume "${WORKSPACE_ROOT}/${WORKSPACE_ID}/agent-state:/agent-state:rw" \
  "${IMAGE}" sleep infinity

echo "container: ${NAME}"
echo "試跑：podman exec -i --workdir /workspace ${NAME} pi --version"
echo "清除：podman rm -f ${NAME}"
