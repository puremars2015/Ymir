// 主要使用流程的端對端驗證（手動執行，尚未進 CI）：ChatGPT 式版面、直接開聊、專案（ADR-0007）。
// 前置：SQL Server、Fake LLM、API（VibeMaker__Harness=Pi）、ng serve 都已啟動。
// 用法：node e2e/chat-flow.mjs <screenshot-dir> [baseUrl]
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const executablePath = process.env.CHROMIUM_PATH;
const browser = await chromium.launch(executablePath ? { executablePath } : {});
const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
const step = (name) => console.log(`✔ ${name}`);
const composer = 'app-composer textarea';
const sendPrompt = async (text) => {
  await page.fill(composer, text);
  await page.press(composer, 'Enter');
};
const waitIdle = () => page.waitForSelector('.turn.live', { state: 'detached', timeout: 60000 });

// 1. 未登入 → 登入頁 → Dev 登入後直接進主畫面
await page.goto(baseUrl);
await page.waitForURL(/\/login/);
const account = `e2e-${Date.now()}`;
await page.fill('input[name=account]', account);
await page.click('button[type=submit]');
await page.waitForSelector('app-new-chat-page h1');
await page.waitForSelector('.sidebar .section-header:has-text("專案")');
step('login → main layout (sidebar + composer)');
await page.screenshot({ path: `${outDir}/01-home.png` });

// 2. 像 ChatGPT 一樣直接在首頁輸入 → 建立未分組對話並送出
await sendPrompt('[create-file] 幫我建立一個檔案');
await page.waitForURL(/\/c\//);
await page.waitForSelector('.turn.assistant:has-text("已完成")', { timeout: 60000 });
await waitIdle();
await page.waitForSelector('.sidebar a.item.active:has-text("幫我建立一個檔案")');
step('started an ungrouped chat from the home composer (title from first message)');
await page.screenshot({ path: `${outDir}/02-chat.png` });

// 3. 重新整理後歷史仍在
await page.reload();
await page.waitForSelector('.turn.assistant:has-text("已完成")');
step('history persisted after reload');

// 4. 建立專案 → 在專案頁直接開聊
await page.click('button[aria-label="新增專案"]');
await page.fill('input[name=projectName]', '行銷網站');
await page.press('input[name=projectName]', 'Enter');
await page.waitForURL(/\/projects\//);
await page.waitForSelector('app-project-page h1:has-text("行銷網站")');
step('created project from the sidebar');
await sendPrompt('[create-file] 在專案裡建立檔案');
await page.waitForURL(/\/c\//);
await page.waitForSelector('.turn.assistant:has-text("已完成")', { timeout: 60000 });
await waitIdle();
await page.waitForSelector('.chat-header .crumb:has-text("行銷網站")');
await page.waitForSelector('.project .nested a.item.active:has-text("在專案裡建立檔案")');
step('chat inside the project (breadcrumb + nested in sidebar)');

// 5. 停止執行中的 Agent
await sendPrompt('[slow] 請慢慢講');
await page.waitForSelector('.turn.live .text:not(:empty)');
await page.click('button[aria-label="停止"]');
await waitIdle();
await page.waitForSelector('button[aria-label="送出"]');
step('cancelled running execution');

// 6. 同一對話繼續（session 續接）
const before = await page.locator('.turn.assistant:not(.live)').count();
await sendPrompt('剛剛做了什麼？');
await page.waitForFunction(
  (n) =>
    document.querySelectorAll('.turn.assistant:not(.live)').length > n &&
    !document.querySelector('.turn.live'),
  before,
  { timeout: 60000 },
);
const last = await page.locator('.turn.assistant').last().innerText();
if (!last.includes('使用者訊息')) throw new Error(`unexpected reply: ${last}`);
step(`session continued: "${last.trim()}"`);
await page.screenshot({ path: `${outDir}/03-project-chat.png` });

// 7. 專案頁列出對話；側邊欄切換對話
await page.click('.chat-header .crumb');
await page.waitForSelector('app-project-page .conversations a:has-text("在專案裡建立檔案")');
await page.screenshot({ path: `${outDir}/04-project.png` });
await page.click('.sidebar .section:has-text("聊天") a.item:has-text("幫我建立一個檔案")');
await page.waitForSelector('.turn.assistant:has-text("已完成")');
step('project page lists its chats; switching chats from the sidebar');

// 8. 窄螢幕：側欄變抽屜
await page.setViewportSize({ width: 390, height: 780 });
await page.waitForSelector('.mobile-bar');
await page.screenshot({ path: `${outDir}/05-mobile.png` });
await page.click('button[aria-label="開啟側邊欄"]');
await page.waitForSelector('.drawer-open');
await page.waitForTimeout(400); // 等抽屜滑入動畫結束再截圖
await page.screenshot({ path: `${outDir}/06-mobile-drawer.png` });
step('mobile drawer');

console.log(`account: ${account}`);
await browser.close();
