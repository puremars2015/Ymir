# Agent Runtime Image

每個使用者一個長駐 container（[ADR-0007](../../docs/adr/0007-one-runtime-per-user.md)），API 以 `podman exec -i` 啟動 `pi --mode rpc`（[ADR-0003](../../docs/adr/0003-pi-rpc-via-podman-exec.md)）。
專案是 container 內的目錄 `/workspace/projects/{id}`，未分組的對話在 `/workspace/chats/{id}`。
參數的唯一來源是 `src/Modules/VibeMaker/Ymir.VibeMaker.Infrastructure/Containers/ContainerCommandBuilder.cs`，並有單元測試保護。
正式環境使用 Rootless Podman；沒有 Linux 主機時可以用 Docker 開發與驗證（[ADR-0005](../../docs/adr/0005-docker-for-development.md)、[Windows 指南](../../docs/guides/windows-docker.md)）。

## 建置

```bash
podman build -t localhost/ymir/agent-runtime:dev -f runtime/agent/Containerfile runtime/agent
```

Pi 版本以 `--build-arg PI_VERSION=x.y.z` 指定（預設 1.0.0）。升級 Pi 時，必須重新錄製 `tests/Ymir.UnitTests/Fixtures/pi-rpc/` 並重跑整合測試。

## Container 內的檔案配置

| 路徑 | 來源 | 用途 |
|---|---|---|
| `/workspace` | host `{WorkspaceRoot}/{workspaceId}/workspace` | 使用者的專案檔案（持久化） |
| `/agent-state` | host `{WorkspaceRoot}/{workspaceId}/agent-state` | Pi 設定（`pi-agent/`，含 `models.json`）與 session 檔（`sessions/`），container 重建後 Agent 不失憶 |
| `/tmp`、`/home/agent` | tmpfs | 暫存；root filesystem 為唯讀 |
| `/opt/ymir/templates` | 映像內建、唯讀 | `/make` 公告 DOCX 模板／建置器與簡報建置指引；不列入使用者成果 |
| `/opt/ymir/python` | 映像內建、唯讀 | Python venv（預設 `python3`），包含鎖定版本的 python-pptx／lxml；預裝 Noto CJK 字型 |

Host 路徑只由 user id 推導（`UserDirectories`：`{WorkspaceRoot}/users/{userId}/workspace`、`agent-state`），API 不接受任何外部傳入的路徑（SA §12）。

## 安全設定（SA §7、§12）

| 旗標 | 目的 |
|---|---|
| rootless（以一般使用者執行 podman） | container 的 root 不是 host 的 root |
| `--userns keep-id:uid=1000,gid=1000` | host 使用者對應到 container 的 `agent`（uid 1000），掛載目錄的檔案擁有者正確 |
| `--cap-drop ALL`、`--security-opt no-new-privileges` | 移除所有 capabilities，禁止提權 |
| `--read-only` + tmpfs | root filesystem 唯讀，只有 `/workspace`、`/agent-state`、tmpfs 可寫 |
| `--pids-limit`、`--memory`、`--cpus` | 資源限制（**需要 cgroups v2 + delegation，見下方**） |
| `--init` | `sleep infinity` 不處理 SIGTERM；用 catatonit 當 PID 1，`podman stop` 才能立即結束 |
| 不掛載 container runtime socket、不使用 `--privileged` | 由單元測試 `ContainerCommandBuilderTests` 檢查 |
| `podman exec --env NAME`（只傳名稱） | LiteLLM key 不會出現在 host 的 `ps` 輸出 |

## 部署主機需求（Sprint 0 驗證後的發現）

1. **cgroups v2 + systemd delegation**：rootless Podman 在 cgroups v1 上會**直接忽略** `--memory`、`--cpus`、`--pids-limit`（只印一行警告）。正式主機必須是 cgroups v2，並為執行 API 的使用者開啟 delegation：

   ```ini
   # /etc/systemd/system/user@.service.d/delegate.conf
   [Service]
   Delegate=cpu cpuset io memory pids
   ```

   驗證：`podman info --format '{{.Host.CgroupsVersion}} {{.Host.CgroupControllers}}'` 應顯示 `v2` 且包含 `memory pids cpu`。
   Sprint 4 會在 `ContainerRuntimeManager` 啟動時檢查並拒絕在不支援的主機上執行。
2. **網路**：rootless 預設 `slirp4netns`（或 Podman 5 的 `pasta`），需要 `/dev/net/tun`。Container 必須能連到 LiteLLM；
   LiteLLM 若在同一台主機，使用 `host.containers.internal`，或改用專用的 Podman network。
3. **套件下載**：Agent 執行 `npm install` / `pip install` 需要對外或內部鏡像（見開發規劃「開發前待確認項目」）。
4. **SELinux 主機**：設定 `VibeMaker:Runtime:SelinuxRelabel=true`，volume 會加上 `:Z`。

## 手動驗證

```bash
# 1. 建置 image（一般使用者）
podman build -t localhost/ymir/agent-runtime:dev -f runtime/agent/Containerfile runtime/agent

# 2. 以 PoC 走完整條路徑：Podman runtime → Pi RPC → Fake LLM（PoC 內建）
dotnet run --project spikes/pi-rpc-poc -- --runtime Podman --network host \
  --prompt "[create-file] 建立檔案" --prompt "第二句"
```

`--network host` 只用於讓 container 連到 PoC 內建、監聽在 127.0.0.1 的 Fake LLM；正式環境不可使用 host network。
完整的驗證步驟與結果見 [`spikes/pi-rpc-poc/README.md`](../../spikes/pi-rpc-poc/README.md)。
