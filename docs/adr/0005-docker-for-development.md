# ADR-0005：Docker 作為開發 / 驗證用的 container engine

- 狀態：已採納
- 日期：2026-10-04

## 背景

正式環境的 Agent runtime 是 Rootless Podman（SA §7、ADR-0003）。目前團隊沒有可以跑 Podman 的 Linux 主機，
但有 Windows 開發機與 Docker Desktop。Docker Desktop 在 WSL2 的 Linux VM 裡執行 container（cgroups v2），
CLI 參數與 Podman 大多相同，可以先用來開發與驗證大部分的 runtime 行為。

## 決策

1. `VibeMaker:Runtime:Provider` 新增 `Docker`（與 `Podman`、`Local` 並列）。兩者共用 `ContainerRuntimeManager` 與 `ContainerCommandBuilder`，差異只有：

   | | Podman（正式） | Docker（開發 / 驗證） |
   |---|---|---|
   | 使用者對應 | `--userns keep-id:uid=1000,gid=1000` | `--user 1000:1000`，並在建立 container 前以一次性 container `chown` 兩個掛載目錄 |
   | 預設網路 | `slirp4netns` | `bridge` |
   | SELinux 重新標記 | `relabel=private`（設定開啟時） | 不適用 |

   共用的安全設定（drop 全部 capabilities、no-new-privileges、唯讀 root fs、資源限制、`--init`、只掛兩個目錄、`exec --env NAME`）兩種 engine 都必須一樣，由 `ContainerCommandBuilderTests` 以兩種 engine 驗證。
2. 掛載一律使用 `--mount type=bind,...`（不用 `--volume`），避免 Windows 路徑的磁碟機冒號造成解析錯誤。
3. **正式環境只支援 Rootless Podman。** Docker 只用於沒有 Linux 主機時的開發與驗證。

## Docker 上驗證不到的項目

- Rootless（Docker Desktop / dockerd 是 root daemon）與 `keep-id` 的檔案擁有者對應
- SELinux 標記
- 正式主機的 cgroups v2 + systemd delegation 設定
- Podman 特有的網路（slirp4netns / pasta）

這些項目仍需在正式的 Linux 主機上依 [`runtime/agent/README.md`](../../runtime/agent/README.md) 驗證後才能上線。

## 影響

- Windows 開發機可以跑完整流程（API → Docker container 內的 Pi → 模型），指南見 [`docs/guides/windows-docker.md`](../guides/windows-docker.md)。
- `Local` runtime 在 Windows 上不可用（需要 `sh` 與 Linux 路徑），DI 會拒絕啟動並提示改用 `Docker`。
- Docker 的 chown 輔助 container 以 root 執行，但不連網路、只掛該 workspace 的兩個目錄、執行完即刪除。
