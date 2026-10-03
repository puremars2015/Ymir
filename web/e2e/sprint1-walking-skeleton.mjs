// Sprint 1 Done Definition 端對端驗證（手動執行，尚未進 CI）。
// 前置：SQL Server、Fake LLM、API（VibeMaker__Harness=Pi）、ng serve 都已啟動。
// 用法：node e2e/sprint1-walking-skeleton.mjs <screenshot-dir> [baseUrl]
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const executablePath = process.env.CHROMIUM_PATH;
const browser = await chromium.launch(executablePath ? { executablePath } : {});
const page = await browser.newPage({ viewport: { width: 1100, height: 760 } });
const step = (name) => console.log(`✔ ${name}`);

// 1. 未登入 → 導向登入頁 → Dev 登入
await page.goto(baseUrl);
await page.waitForURL(/\/login/);
await page.fill('input[name=account]', `e2e-${Date.now()}`);
await page.click('button[type=submit]');
await page.waitForSelector('h1:has-text("我的 Workspace")');
step('dev login');

// 2. 建立 Workspace 與 Conversation
await page.fill('input[name=name]', 'Todo App');
await page.click('button:has-text("建立")');
await page.waitForURL(/\/workspaces\//);
await page.fill('input[name=title]', '建立檔案測試');
await page.click('button:has-text("新對話")');
await page.waitForURL(/\/conversations\//);
const conversationUrl = page.url();
step('workspace + conversation created');

// 3. 送出 [create-file]，看到串流與工具事件，完成後歷史保存
await page.fill('input[name=prompt]', '[create-file] 幫我建立一個檔案');
await page.click('button:has-text("送出")');
// Fake LLM 很快，工具事件可能在畫面更新前就結束，所以只驗證最後保存的結果。
await page.waitForSelector('.bubble.assistant:has-text("已完成")', { timeout: 60000 });
await page.waitForSelector('.bubble.live', { state: 'detached' });
step('agent response streamed and saved');
await page.screenshot({ path: `${outDir}/sprint1-chat.png` });

// 4. 重新整理後歷史訊息仍在
await page.reload();
await page.waitForSelector('.bubble.assistant:has-text("已完成")');
const count = await page.locator('.bubble').count();
if (count < 2) throw new Error(`expected history after reload, got ${count} bubbles`);
step(`history persisted after reload (${count} messages)`);

// 5. 停止執行中的 Agent
await page.fill('input[name=prompt]', '[slow] 請慢慢講');
await page.click('button:has-text("送出")');
await page.waitForSelector('.bubble.live .text:not(:empty)');
await page.click('button:has-text("停止")');
await page.waitForSelector('.bubble.live', { state: 'detached', timeout: 30000 });
await page.waitForSelector('button:has-text("送出")');
step('cancelled running execution');

// 6. 同一對話繼續（session 續接）
const assistantCount = await page.locator('.bubble.assistant:not(.live)').count();
await page.fill('input[name=prompt]', '剛剛做了什麼？');
await page.click('button:has-text("送出")');
await page.waitForFunction(
  (before) => document.querySelectorAll('.bubble.assistant:not(.live)').length > before && !document.querySelector('.bubble.live'),
  assistantCount,
  { timeout: 60000 },
);
const last = await page.locator('.bubble.assistant').last().innerText();
if (!last.includes('使用者訊息')) throw new Error(`unexpected reply: ${last}`);
step(`session continued: "${last.trim()}"`);
await page.screenshot({ path: `${outDir}/sprint1-chat-history.png` });

console.log(`conversation: ${conversationUrl}`);
await browser.close();
