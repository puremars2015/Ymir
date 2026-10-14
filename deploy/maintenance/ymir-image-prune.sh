#!/usr/bin/env bash
# Ymir container image 回收：每次部署都會建立新的 image，container 以 --rm 自動回收，image 卻會一直累積。
# 依 store 分別處理（root 的 rootful Podman、ymir 與 itadmin 的 rootless Podman），只動 localhost/ymir/* 與指定 store 的 dangling image：
#   - 任何 container（執行中或已停止）使用中的 image 一律保留（podman rmi 不加 -f，被使用時本來就會拒絕）；
#   - 正式設定指向的 image 一律保留（API Quadlet 的 Image=、runtime host 的 VibeMaker__Runtime__Image）；
#   - 每個 localhost/ymir/* repository 另外保留最新的 KEEP 個（預設 3），供回復舊版。
# 共用主機上其他專案的 image（itadmin store）不碰；dangling image 只清 root 與 ymir store。
# 用法：sudo ymir-image-prune.sh            # 只列出會刪除的 image（dry run）
#       sudo ymir-image-prune.sh --apply    # 實際刪除
set -euo pipefail

APPLY=0
[[ ${1:-} == --apply ]] && APPLY=1
KEEP=${YMIR_IMAGE_KEEP:-3}
QUADLET=${YMIR_API_QUADLET:-/etc/containers/systemd/ymir-api.container}
RUNTIME_ENV=${YMIR_RUNTIME_HOST_ENV:-/etc/ymir/runtime-host.env}
# 格式：名稱:帳號:是否清 dangling（空白分隔）。root 代表 rootful store。
STORES=${YMIR_IMAGE_STORES:-"root:root:1 ymir:ymir:1 itadmin:itadmin:0"}

[[ $EUID -eq 0 ]] || { echo "需要 root（sudo）執行" >&2; exit 1; }
cd /tmp

# 正式設定指向的 image（可能是 ID 或 name:tag），解析成完整 ID 後保護。
pinned_refs() {
  [[ -r $QUADLET ]] && sed -n 's/^Image=//p' "$QUADLET"
  [[ -r $RUNTIME_ENV ]] && sed -n 's/^VibeMaker__Runtime__Image=//p' "$RUNTIME_ENV" | tr -d '"'"'"
  return 0
}

total_freed=0
for entry in $STORES; do
  IFS=: read -r name user dangling <<<"$entry"
  if [[ $user == root ]]; then
    P=(podman)
  else
    id "$user" >/dev/null 2>&1 || { echo "== $name：帳號不存在，略過"; continue; }
    uid=$(id -u "$user")
    P=(runuser -u "$user" -- env HOME="$(getent passwd "$user" | cut -d: -f6)" XDG_RUNTIME_DIR=/run/user/$uid podman)
  fi
  echo "== store: $name"

  declare -A protect=()
  # container 使用中的 image（含已停止的）。
  while read -r img; do img=${img#sha256:}; [[ -n $img ]] && protect[$img]=container; done < <("${P[@]}" ps -a --no-trunc --format '{{.ImageID}}' 2>/dev/null)
  while read -r ref; do
    [[ -z $ref ]] && continue
    full=$("${P[@]}" image inspect --format '{{.Id}}' "$ref" 2>/dev/null || true)
    full=${full#sha256:}
    [[ -n $full ]] && protect[$full]=pinned
  done < <(pinned_refs)

  # localhost/ymir/* 依 repository 分組，依建立時間由新到舊；同一個 image 有多個 tag 時只算一次。
  declare -A seen=() kept=()
  candidates=()
  while IFS='|' read -r repo id _; do
    id=${id#sha256:}
    [[ -n ${seen[$id]:-} ]] && continue
    seen[$id]=1
    if [[ -n ${protect[$id]:-} ]]; then
      echo "   keep  ${id:0:12} $repo（${protect[$id]}）"
      continue
    fi
    n=${kept[$repo]:-0}
    if (( n < KEEP )); then
      kept[$repo]=$((n + 1))
      echo "   keep  ${id:0:12} $repo（最新 $((n + 1))/$KEEP）"
    else
      candidates+=("$id")
    fi
  # CreatedAt 為「YYYY-MM-DD hh:mm:ss +0000 UTC」，字串排序即時間排序。
  done < <("${P[@]}" images --no-trunc --format '{{.Repository}}|{{.ID}}|{{.CreatedAt}}' --filter reference='localhost/ymir/*' \
             | sort -t'|' -k3,3r)

  if (( dangling )); then
    while read -r id; do
      id=${id#sha256:}
      [[ -z $id || -n ${seen[$id]:-} || -n ${protect[$id]:-} ]] && continue
      seen[$id]=1
      candidates+=("$id")
    done < <("${P[@]}" images --no-trunc -q --filter dangling=true)
  fi

  for id in "${candidates[@]}"; do
    size=$("${P[@]}" image inspect --format '{{.Size}}' "$id" 2>/dev/null || echo 0)
    tags=$("${P[@]}" image inspect --format '{{join .RepoTags ","}}' "$id" 2>/dev/null || true)
    if (( APPLY )); then
      if "${P[@]}" rmi "$id" >/dev/null 2>&1; then
        echo "   rm    ${id:0:12} ${tags:-<none>} $((size / 1000000)) MB"
        total_freed=$((total_freed + size))
      else
        echo "   skip  ${id:0:12} ${tags:-<none>}（仍被使用或有子 image）"
      fi
    else
      echo "   would ${id:0:12} ${tags:-<none>} $((size / 1000000)) MB"
      total_freed=$((total_freed + size))
    fi
  done
  unset protect seen kept
done

if (( APPLY )); then
  echo "已刪除約 $((total_freed / 1000000)) MB（共用 layer 只在最後一個 image 刪除時才釋放）"
else
  echo "dry run：可回收約 $((total_freed / 1000000)) MB；加上 --apply 才會刪除"
fi
