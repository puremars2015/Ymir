#!/bin/bash
# Claude Code 雲端 session 啟動時安裝開發工具與相依套件（可重複執行）。
# - .NET 10 SDK（Ubuntu apt）
# - Node 24 LTS（Angular 22 需要 Node >= 22.22.3 或 >= 24.15）
# - Pi coding agent（整合測試會啟動真正的 Pi 程序）
# - dotnet restore、web/ 的 npm 套件
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

NODE_VERSION="24.21.0"
NODE_DIR="/opt/node24"
PI_VERSION="1.0.0"
ENV_FILE="${CLAUDE_ENV_FILE:-/dev/null}"

cd "${CLAUDE_PROJECT_DIR:-$(dirname "$0")/../..}"

# .NET 10 SDK
if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks | grep -q '^10\.'; then
  apt-get update -qq
  DEBIAN_FRONTEND=noninteractive apt-get install -y -qq dotnet-sdk-10.0
fi

# Node 24 LTS
if [ "$("${NODE_DIR}/bin/node" --version 2>/dev/null)" != "v${NODE_VERSION}" ]; then
  mkdir -p "${NODE_DIR}"
  curl -fsSL "https://nodejs.org/dist/v${NODE_VERSION}/node-v${NODE_VERSION}-linux-x64.tar.xz" \
    | tar -xJ -C "${NODE_DIR}" --strip-components=1
fi
export PATH="${NODE_DIR}/bin:${PATH}"

{
  echo "export PATH=\"${NODE_DIR}/bin:\$PATH\""
  echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
  echo "export DOTNET_NOLOGO=1"
} >> "${ENV_FILE}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# Pi coding agent（版本需與 runtime/agent/Containerfile 一致）
if [ "$(pi --version 2>/dev/null || true)" != "${PI_VERSION}" ]; then
  npm install -g --ignore-scripts "@earendil-works/pi-coding-agent@${PI_VERSION}"
fi

# 相依套件
dotnet restore Ymir.slnx
(cd web && npm install --no-audit --no-fund)
