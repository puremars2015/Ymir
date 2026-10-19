# 部署流程紀錄（流水帳）

腳本與容器的固定對照見 [Container 部署操作索引](../guides/container-deployment.md)；此處保存**每次實際執行**的範圍、順序與結果。不是另一套自動部署工具。

## 紀錄索引

| 日期 | 紀錄 | 性質／結果 |
|---|---|---|
| 2026-10-19 | [更新 8e47b6e 與安裝私有 MCP Gateway](2026-10-19-release-8e47b6e.md) | 產物／隔離驗證完成，已開本機 sudo 視窗，正式切換結果待讀回 |
| 2026-10-19 | [模型代理恢復、開機修正及部署基線盤點](2026-10-19-model-proxy-boot.md) | 只恢復 proxy／安裝 user drop-in；未重建其他服務，未 reboot |

較早的部署歷史仍保留在 [Sprint 6 看板](../progress/board-sprint-6.md)，包含 #068、#085、#090、#093；沒有逐一重新實測，不搬寫成新的成功部署。

## 每次部署範本

建立 `YYYY-MM-DD-簡短主題.md`，同日多次執行用獨立檔名；完成後新增上方索引及 Sprint 留言。

```markdown
# 日期 · 部署／修復主題

## 授權與範圍
- 主機／環境：
- 時區、開始／結束時間、執行者與授權方式（不含密碼）：
- 狀態：準備中／已執行／成功／已回復／待處理
- Git SHA、CI URL／結果；注意它不一定等於正在服務的 API release：
- 更新與不更新的服務：

## 逐服務版本與工具
| 管理帳號 | 容器／主機服務 | 舊 image ID／release | 新 image ID／release | script／配置來源 | 動作 |
|---|---|---|---|---|---|

## 前置檢查與備份
- 排空 Queued／Running、部署鎖、可用資源：
- 兩模組 migration 差異／隔離測試／schema 相容性：
- 私有備份位置、範圍、checksum／verify 結果（不貼內容）：
- 事先審查的回復方案與資料風險：

## 實際執行順序
| 時間 | 帳號 | command／script（去機密） | 影響服務 | exit code／結果 |
|---|---|---|---|---|

## 驗收
| 項目 | 結果／證據摘要 |
|---|---|
| 精確 image ID／publish 路徑、掛載／非 root 安全設定 | |
| SQL／LiteLLM／RuntimeHost／API readiness | |
| Agent → 模型入口 | |
| 公開登入、真 SSO、對話／Pi SSE、附件與舊檔案 | |
| 用量／可選 MCP／OneDrive／SiteHost | |
| 冷開機／備份還原 | |

## 回復／待辦與結論
- 是否回復、實際操作／結果：
- 未驗證項目與負責人／下一步：
- 公開入口最終狀態、資料風險、相關看板／CI：
```

沒有執行的命令不能填 exit code 0；單一 health 200 不等於登入／真模型／資料還原驗收成功。不得把含 secret 的部署包、env、SQL 備份、keyring 或完整 inspect 存入此目錄。
