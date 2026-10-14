# 主機維護

## Image 回收（`ymir-image-prune`）

Container 以 `--rm` 執行、結束後自動回收，但 image 不會：每次部署 API 都會建立一個新的 `localhost/ymir/api:<commit>`（約 360 MB），建置也會留下沒有 tag 的中間 image。`ymir-image-prune.sh` 依下列規則定期清理：

| Store | 範圍 | dangling（無 tag）image |
|---|---|---|
| `root`（rootful Podman：API、正式 nginx、Tunnel） | `localhost/ymir/*` | 清理 |
| `ymir`（rootless：Agent runtime、LiteLLM proxy） | `localhost/ymir/*` | 清理 |
| `itadmin`（rootless：開發建置、與其他專案共用） | `localhost/ymir/*` | **不清理**（可能是其他專案的建置快取） |

一律保留：

- 任何 container（執行中或已停止）使用中的 image；`podman rmi` 不加 `-f`，仍被使用時會拒絕刪除。
- 正式設定指向的 image：API Quadlet（`/etc/containers/systemd/ymir-api.container`）的 `Image=`、runtime host（`/etc/ymir/runtime-host.env`）的 `VibeMaker__Runtime__Image`。
- 每個 `localhost/ymir/*` repository 最新的 `YMIR_IMAGE_KEEP` 個（預設 3），供回復舊版。回復時 Quadlet 的備份（`/var/backups/ymir/*/ymir-api.container`）指向的 image 如果已被回收，需要重新建置該 commit。

不會碰其他專案的 image、base image（`mcr.microsoft.com/...`、`docker.io/...`）或 volume。

### 使用

```bash
sudo bash deploy/maintenance/ymir-image-prune.sh            # dry run：只列出會刪除的 image
sudo bash deploy/maintenance/ymir-image-prune.sh --apply    # 實際刪除
```

設定（環境變數）：`YMIR_IMAGE_KEEP`（保留數）、`YMIR_IMAGE_STORES`（`名稱:帳號:是否清 dangling`，空白分隔，預設 `root:root:1 ymir:ymir:1 itadmin:itadmin:0`）、`YMIR_API_QUADLET`、`YMIR_RUNTIME_HOST_ENV`。

### 每週自動執行

```bash
sudo install -m 755 deploy/maintenance/ymir-image-prune.sh /usr/local/sbin/ymir-image-prune
sudo install -m 644 deploy/maintenance/ymir-image-prune.service deploy/maintenance/ymir-image-prune.timer /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now ymir-image-prune.timer
systemctl list-timers ymir-image-prune.timer        # 下次執行時間（每週日 04:00 前後）
journalctl -u ymir-image-prune.service              # 每次刪除了哪些 image
```

更新腳本後要重新 `install` 到 `/usr/local/sbin`。
