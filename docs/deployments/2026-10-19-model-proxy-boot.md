# 2026-10-19 · 模型代理恢復、開機修正及部署基線盤點

## 授權與範圍

- 環境：目前 Ubuntu 正式主機 `ub01`；時間使用 CST（UTC+8）。
- 使用者要求診斷對話「模型服務發生錯誤」，同意修復並在本機終端輸入 sudo 密碼；其後要求修正開機程序及新增各 container 的 script／流程紀錄。
- 結果：模型 proxy 恢復、開機等待 drop-in 安裝成功。**不是整站新版部署**，未重建 API／資料庫／Agent，未 reboot。
- 修正來源：`20d86ae`；安裝結果進度提交 `132f5c3` 的 [CI 39196399120](https://github.com/puremars2015/Ymir/actions/runs/39196399120) 已 completed／success。
- 文件盤點只讀取 repo／systemd 去機密欄位及 itadmin 容器名稱／狀態；本次沒有重新取得全部 root／ymir store 的 image ID，不能當成全量 image manifest。

## 逐服務版本與工具

| 管理帳號 | 容器／主機服務 | 操作前 → 操作後 | 實際工具／設定 |
|---|---|---|---|
| ymir | `ymir-litellm-proxy` | failed／容器不存在 → active；後續只加等待／退避設定，image 未更換 | `systemctl --user start`；[fix-boot.sh](../../deploy/runtime-host/litellm-proxy/fix-boot.sh) 安裝 [boot-readiness.conf](../../deploy/runtime-host/litellm-proxy/boot-readiness.conf) |
| ymir（system service） | `ymir-litellm-bridge` | active → 不變 | 只讀取 unit／狀態，未重啟 |
| root | `ymir-api`、`ymir-web`、`ymir-cloudflared-production` | 不變 | 未執行應用／入口上版；API health 200 |
| itadmin | `ymir-sql`、`litellm_litellm-db_1`、`litellm_litellm_1` | 不變 | `podman ps`；SQL／LiteLLM running，PostgreSQL healthy；未重建 |
| ymir | 既有使用者 Agent | 不變 | 在既有 running Agent 內用 Node fetch 測模型 liveliness；無憑證、未呼叫推論 |
| ymir（system service） | RuntimeHost | 不變 | 文件盤點讀回 `WorkingDirectory=/opt/ymir/releases/348b64c/runtime-host`；這不代表 API 版本也相同 |

## 根因與實際流程

| 順序 | 時間／操作 | 結果與限制 |
|---|---|---|
| 1 | 讀取 10/19 11:20、11:21、11:32 的 Pi 錯誤及 LiteLLM liveliness | 三次 `Connection error`；LiteLLM HTTP 200，未看到相應模型請求；不是先前的 413 |
| 2 | 本機 sudo 終端，以 ymir 身分查 `podman ps -a`、proxy status、user journal | 10/17 13:18 開機時 `/run/ymir-litellm` 不存在，proxy 連續失敗五次後被 start limit 阻斷；system bridge 稍後才啟動 |
| 3 | `systemctl --user start ymir-litellm-proxy.service` | 10/19 11:42:22 CST proxy active；未換映像 |
| 4 | 在現存 Agent 內 fetch `http://litellm:4000/health/liveliness` | HTTP 200，內容為 liveliness 回應；不是上游真模型推論成功證明 |
| 5 | 新增並測試 Quadlet／drop-in 等待與退避；使用者授權執行 `sudo bash deploy/runtime-host/litellm-proxy/fix-boot.sh` | 前置檢查 bridge enabled／active、socket、linger；備份後 reload user manager 並確保 proxy started，安裝 exit code 0；未重啟已運作容器 |
| 6 | 讀回設定與 API health | `WantedBy=default.target`、active、start limit interval 0、restart 10s、ExecStartPre 最多等 socket 60 秒；API HTTP 200 |
| 7 | 文件化各容器 script／流程與主機規格缺口 | 只改文件，未執行額外部署、migration 或 container rebuild |

早期診斷開終端曾逾時；`sudo -iu ymir` 也因 nologin 被拒。後改用 `sudo -H -u ymir env XDG_RUNTIME_DIR=/run/user/$(id -u ymir)` 執行檢查，未更改帳號登入設定；失敗嘗試不計為修復成功。

## 備份、安裝位置與回復

- 本次沒有 schema／DB／secret 變更，沒有執行 migration 或資料還原。
- drop-in 備份：`/var/backups/ymir-litellm-boot-Qq4GUm8p`；備份目標為原同名 drop-in，原先不存在則以 `previously-absent` 標示。這不是資料庫／workspace 備份。
- 安裝目標：`/home/ymir/.config/systemd/user/ymir-litellm-proxy.service.d/90-boot-readiness.conf`。
- 回復方式：先核對備份；只還原原 drop-in，或在有 `previously-absent` 時移除這次新增的單一檔案，再以 ymir 執行 user `daemon-reload`。不覆寫 Quadlet、nginx、API env 或資料。
- 實際未執行回復；也未以停掉正式 bridge／proxy 模擬開機故障。

## 驗收與待辦

| 項目 | 結果 |
|---|---|
| socket 等待／延遲就緒／逾時後再次啟動／普通檔案拒絕 | 8 項本機 Python regression tests 通過，含範本一致與 Bash 語法 |
| Quadlet 設定生成 | `--user --dryrun` 確認等待／退避設定保留 |
| 正式安裝／user unit 設定讀回 | 通過、installer exit code 0 |
| Agent → LiteLLM liveliness | HTTP 200 |
| API health | HTTP 200 |
| Git／CI | 開機修正已 push；`132f5c3` CI 全通過 |
| 真登入／真模型回答／Pi SSE／可選外掛 | 本次未做端到端驗收，待使用者重送訊息／另行驗收 |
| 完整冷開機／備份還原 | 未執行，需另排維護時段 |
| 主機專用部署配置納入版控、統一增量上版器 | 只完成[盤點與流程紀錄](../guides/container-deployment.md)，尚未實作自動化 |

相關歷史與原始處理摘要見 [Sprint 6 看板 #096／#097](../progress/board-sprint-6.md)。
