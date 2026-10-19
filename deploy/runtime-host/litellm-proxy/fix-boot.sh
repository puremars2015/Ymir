#!/usr/bin/env bash
# 僅修正既有 proxy 的開機等待；不重啟正在服務的代理、API 或資料庫。
set -euo pipefail
umask 077
if [[ $EUID -ne 0 ]]; then
    echo '請以 sudo bash 執行本腳本。' >&2
    exit 1
fi
source_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
uid=$(id -u ymir)
home=$(getent passwd ymir | cut -d: -f6)
quadlet="$home/.config/containers/systemd/ymir-litellm-proxy.container"
dropin_dir="$home/.config/systemd/user/ymir-litellm-proxy.service.d"
target="$dropin_dir/90-boot-readiness.conf"
[[ -f "$quadlet" && -d "/run/user/$uid" ]]
[[ $(loginctl show-user ymir -p Linger --value) == yes ]] || {
    echo 'ymir 必須已啟用 linger；請先依 RuntimeHost 安裝指南設定。' >&2
    exit 1
}
systemctl is-enabled --quiet ymir-litellm-bridge.service
systemctl is-active --quiet ymir-litellm-bridge.service
[[ -S /run/ymir-litellm/litellm.sock ]]
# 不接受既有 symlink 作為更新目標，避免意外覆蓋其他設定。
[[ ! -L "$dropin_dir" && ! -L "$target" ]]
backup=$(mktemp -d /var/backups/ymir-litellm-boot-XXXXXXXX)
if [[ -e "$target" ]]; then
    cp -a -- "$target" "$backup/90-boot-readiness.conf"
else
    touch "$backup/previously-absent"
fi
echo "備份：$backup"
install -d -o ymir -g ymir -m 700 "$dropin_dir"
install -o root -g root -m 644 "$source_dir/boot-readiness.conf" "$target"
cd /home
userctl() {
    runuser -u ymir -- env HOME="$home" XDG_RUNTIME_DIR="/run/user/$uid" systemctl --user "$@"
}
userctl daemon-reload
# daemon-reload 不影響正在執行的容器；若代理仍失敗才重新啟動。
userctl reset-failed ymir-litellm-proxy.service
userctl start ymir-litellm-proxy.service
userctl is-active --quiet ymir-litellm-proxy.service
userctl show ymir-litellm-proxy.service -p ActiveState -p RestartUSec -p StartLimitIntervalUSec -p ExecStartPre -p WantedBy
echo '已套用；未重啟主機。完整冷開機驗收仍需安排維護時段。'
