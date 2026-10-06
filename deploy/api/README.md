# Ymir API container

架構決策見 [ADR-0008](../../docs/adr/0008-containerized-api-runtime-host.md)。Image 由 [`src/Ymir.Api/Containerfile`](../../src/Ymir.Api/Containerfile) 建置，包含 API 與 Angular build（同源，ADR-0002）。

## 容器能碰到的主機資源

| 主機 | 容器內 | 用途 |
|---|---|---|
| `/run/ymir-runtime/` | `/run/ymir-runtime/` | runtime host 的 Unix socket。要連線必須同時具備 socket 的 group 權限與 token |
| `/srv/ymir/api/keys/` | `/var/lib/ymir/keys/` | Data Protection 金鑰；容器重建後，登入 cookie 仍然有效 |
| `127.0.0.1:5080`（host network） | 同左 | 只綁主機的 loopback，外部只能經由 cloudflared 進來（ADR-0006） |

- **沒有掛載的東西**：
  - container runtime socket（`podman.sock` / `docker.sock`）；
  - 使用者的 workspace；
  - cloudflared 憑證。
- **API 不能直接操作 Agent container**：一律經由 runtime host（[`deploy/runtime-host`](../runtime-host/README.md)）。
- **容器本身的限制**：檔案系統唯讀、drop 全部 capabilities、`no-new-privileges`，以 .NET image 內建的非 root 使用者（uid 1654）執行。

## 啟動

> 依賴的元件：
> - runtime host（先依 [`deploy/runtime-host/README.md`](../runtime-host/README.md) 安裝）；
> - SQL Server；
> - LiteLLM（[`deploy/litellm`](../litellm/README.md)）。
>
> 「rootless Podman 主機上的 runtime host + group 權限」這一段在雲端沙箱無法驗證，**待使用者環境確認**。

```bash
sudo install -d -o 1654 -g 1654 -m 700 /srv/ymir/api/keys
cd deploy/api
cp .env.example .env            # 填入連線字串、token、LiteLLM master key、YMIR_RUNTIME_GID
docker compose up -d --build    # 或 podman compose
docker compose logs -f api
curl -I http://127.0.0.1:5080/  # 200，Angular 首頁
```

第一次啟動前先套用 migration。Production 不會自動 migrate，要在 repo 執行 `dotnet ef database update`，兩個 DbContext 都要跑。

## 用哪個 container engine 跑 API

- **Docker、rootful Podman**：`group_add` 直接把 `ymir-runtime` 的 gid 加給容器使用者，即可連 socket。這是建議做法。
- **Rootless Podman，以 `ymir` 以外的帳號執行**：容器內的 gid 會被 user namespace 對應掉，`group_add` 無效。要改用以下設定，詳細行為依 Podman 版本而定：
  - `podman run --userns keep-id --group-add keep-groups ...`
  - 該帳號本身要屬於 `ymir-runtime`；
  - 需要 crun。
- **不要用 `ymir` 帳號跑 API 容器**：API 被攻破時，攻擊者會拿到可以直接操作 rootless Podman 的帳號，等於繞過 runtime host。

## 疑難排解

| 症狀 | 原因 / 處理 |
|---|---|
| 送出訊息後顯示「無法啟動執行環境」，API log 有 `Permission denied` / `Connection refused` | socket 不存在，或容器沒有 `ymir-runtime` 權限：確認 runtime host 正在執行（`systemctl status ymir-runtime-host`），且 `YMIR_RUNTIME_GID` 正確 |
| API log 出現 `Runtime host ensure failed with status 401` | `VibeMaker__Runtime__Remote__Token` 與 runtime host 的 `RuntimeHost__Token` 不一致 |
| 容器重建後所有人都被登出 | `/var/lib/ymir/keys` 沒有掛載，或目錄不能寫入（擁有者應為 1654） |
| 啟動失敗：`VibeMaker:LiteLlm:MasterKey and BaseUrl are required` | Production 必須設定 LiteLLM（ADR-0004） |
