# ADR-0015：交付成果與工作檔案分離

- 日期：2026-10-07
- 狀態：已接受

## 背景

Agent 為讀取 PDF 安裝工具時，npm 的 package.json 與 lockfile 會被檔案差異偵測誤列為下載成果。檔名黑名單也會錯誤排除網站必要的設定檔。

## 決定

每次執行提供 deliverables/{executionId}/ 交付目錄，透過平台附加 system prompt 指定。工具與暫存使用隱藏的 .ymir/tools/ 及 .ymir/tmp/，Agent 映像預裝 pdftotext。

只有成功執行結束後由後端掃描驗證、寫入 execution_artifacts 的檔案才是交付成果。成果登記與 Completed 狀態一起保存，完成 SSE 事件在保存後才發布。沒有有效紀錄時不回退到檔案差異偵測。

成果端點驗證對話／專案擁有者、成功 execution、已登記檔案以及 runtime 內的實際路徑；拒絕 symlink、隱藏檔與 node_modules。工作檔下載不接受 deliverables 路徑，避免發布未完成成果。成果目錄限制 500 個檔案、合計 200 MiB，超過時不登記並記錄診斷。

回覆提供單檔或該次多檔 ZIP。檔案面板預設成果，另可切換專案檔案；同專案共享成果。歷史工作檔保留，不推測歷史成果。資料庫新增成果中繼資料表及 execution 外鍵；不變更 RuntimeHost 協定與允許的工作目錄。

## 影響

Agent 必須遵守明確交付約定。登記失敗仍保存成功的對話回覆，但不提供下載推薦，並寫入伺服器診斷。已交付成果應視為不可變，更新時建立新 execution 成果；下載發現大小或修改時間改變時拒絕提供，避免下載與成果紀錄不一致。
