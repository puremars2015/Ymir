# 網站託管（ADR-0016）

使用者在對話的「檔案」面板把含有 `index.html` 的目錄發布成網站，網址為 `https://<8 碼代碼>.<網站網域>/`。

- **API**：複製檔案到網站 volume（可寫）。
- **SiteHost**（`src/Ymir.SiteHost`）：依 Host 找到網站、檢查權限、提供檔案（唯讀）。

## 設定

| 元件 | 設定 |
|---|---|
| API | `Ymir__Sites__BaseUrl=https://sites.example.com`、`Ymir__Sites__Root=/srv/ymir/sites`（可寫 volume）；限制 `Ymir__Sites__MaxBytes`、`MaxFiles`、`MaxSitesPerUser`、`KeepVersions` |
| SiteHost | `site-host.env.example`（BaseUrl 與 API 相同、Root 唯讀、PlatformUrl、Data Protection 金鑰目錄、資料庫連線） |
| DNS / TLS / Tunnel | `*.sites.example.com` 萬用字元 DNS 指向 Tunnel；cloudflared ingress 加一條 `hostname: "*.sites.example.com"` → `http://127.0.0.1:5300` |

網站網域**必須**與 Ymir 登入網域不同（例如 `ymir.example.com` 與 `*.sites.example.com`，或完全不同的網域），網站腳本才拿不到平台 cookie。

## 安裝

1. `useradd --system ymir-sites`；`dotnet publish src/Ymir.SiteHost -c Release -o /opt/ymir/site-host`。
2. 建立 `/srv/ymir/sites`：API 帳號可寫，`ymir-sites` 可讀。
3. 複製 `site-host.env.example` 到 `/etc/ymir/site-host.env`（600）並填值；安裝 `ymir-site-host.service`，`systemctl enable --now ymir-site-host`。
4. 健康檢查：`curl -H 'Host: x' http://127.0.0.1:5300/.ymir/health`。

## 本機驗證

沙箱用 `*.localhost`（Chromium 會解析到 127.0.0.1）：API 設定 `Ymir__Sites__BaseUrl=http://sites.localhost:5300`，SiteHost 聽 5300，見 `web/e2e/sites-flow.mjs` 開頭。

**未驗證、待使用者環境確認**：真實網域、萬用字元 DNS / TLS、Tunnel 路由、正式 volume 權限。
