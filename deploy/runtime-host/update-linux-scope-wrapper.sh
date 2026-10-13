#!/usr/bin/env bash
# 熱更新原有 wrapper；不重啟服務／容器、不改密鑰、不接觸使用者工作檔案。
set -Eeuo pipefail
umask 077
fail() { printf '錯誤：%s\n' "$*" >&2; exit 1; }
if [[ ${1:-} == --help ]]; then
    printf '使用：sudo bash deploy/runtime-host/update-linux-scope-wrapper.sh\n關閉 systemd argv 展開，並以現存 Agent 驗證真實上傳腳本。\n'
    exit 0
fi
[[ $# == 0 && $EUID == 0 ]] || fail '請使用 sudo 執行。'
exec 9>/run/lock/ymir-production-v2.lock
flock -n 9 || fail '另一個 Ymir 部署程序正在執行。'
directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
target=/usr/local/libexec/ymir-podman-scope
[[ -f $target ]] || fail '現有 wrapper 尚未安裝。'
uid=$(id -u ymir)
mapfile -t names < <(cd / && runuser -u ymir -- env HOME=/home/ymir XDG_RUNTIME_DIR="/run/user/$uid" /usr/bin/podman ps --filter label=ymir.user-id --format '{{.Names}}' 9>&-)
[[ ${#names[@]} -gt 0 ]] || fail '沒有執行中的 Agent 可測試，未修改設定。'
for name in "${names[@]}"; do
    [[ $name =~ ^ymir-user-[0-9a-f]{32}$ ]] || fail 'Agent 名稱不符預期。'
    python3 "$directory/test-podman-scope.py" "$name" --owner ymir 9>&-
done
backup=/var/backups/ymir/wrapper-$(date +%Y%m%d-%H%M%S)-$$
install -d -m 700 "$backup"
cp -a "$target" "$backup/ymir-podman-scope"
printf 'wrapper 備份：%s\n' "$backup"
temporary=$(mktemp /usr/local/libexec/.ymir-podman-scope.XXXXXX)
recover() {
    local status=$?
    trap - ERR INT TERM
    set +e
    cp -a "$backup/ymir-podman-scope" "$temporary"
    mv -fT -- "$temporary" "$target"
    printf '更新未完成，已嘗試恢復 wrapper。\n' >&2
    (( status != 0 )) || status=1
    exit "$status"
}
trap recover ERR INT TERM
install -o root -g root -m 755 "$directory/ymir-podman-scope" "$temporary"
mv -fT -- "$temporary" "$target"
for name in "${names[@]}"; do
    python3 "$directory/test-podman-scope.py" "$name" --owner ymir --wrapper "$target" 9>&-
done
[[ $(curl -fsS --max-time 10 http://127.0.0.1:5080/health) == Healthy ]]
trap - ERR INT TERM
printf '\nwrapper 更新完成，真實寫檔腳本及 API 健康檢查通過。\n沒有重啟服務或修改你的檔案；請重新上傳原檔。\n'
