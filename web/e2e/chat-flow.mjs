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

// 1. 未登入 → 登入頁 → Dev 登入後直接進主畫面（Development 的開發登入收在 <details> 內）
await page.goto(baseUrl);
await page.waitForURL(/\/login/);
const account = `e2e-${Date.now()}`;
await page.waitForSelector('details.dev');
if (!(await page.locator('details.dev').evaluate((d) => d.open))) {
  await page.click('details.dev summary');
}
await page.fill('input[name=devAccount]', account);
await page.click('details.dev button[type=submit]');
await page.waitForSelector('app-new-chat-page h1');
await page.waitForSelector('.sidebar .section-header:has-text("專案")');
step('login → main layout (sidebar + composer)');

// 1b. 個人設定：個人 global system prompt
await page.click('.user a:has-text("設定")');
await page.waitForURL(/\/settings/);
await page.fill('textarea[name=systemPrompt]', '請一律用繁體中文回答。');
await page.click('app-settings-page button[type=submit]');
await page.waitForSelector('app-settings-page [role=status]:has-text("已儲存")');
await page.screenshot({ path: `${outDir}/07-settings.png` });
await page.reload();
await page.waitForFunction(
  () => {
    const area = document.querySelector('textarea[name=systemPrompt]');
    return area && !area.disabled && area.value.length > 0;
  },
  null,
  { timeout: 15000 },
);
const savedPrompt = await page.inputValue('textarea[name=systemPrompt]');
if (savedPrompt !== '請一律用繁體中文回答。')
  throw new Error(`personal prompt not saved: ${savedPrompt}`);
step('personal system prompt saved');
await page.click('a.new-chat');
await page.waitForSelector('app-new-chat-page h1');

// 1c. 首頁選模型（需要 API 設定兩個以上的 VibeMaker:Models）
const modelOptions = await page.locator('app-new-chat-page select[name=model] option').count();
if (modelOptions >= 2) {
  await page.selectOption('app-new-chat-page select[name=model]', { index: 1 });
}
await page.screenshot({ path: `${outDir}/01-home.png` });
const chosenModel = await page.inputValue('app-new-chat-page select[name=model]');
step(`model picker (${modelOptions} models, chose ${chosenModel})`);

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
const rememberedModel = await page.inputValue('app-chat-page select[name=model]');
if (rememberedModel !== chosenModel)
  throw new Error(`conversation did not remember model: ${rememberedModel}`);
step(`history persisted after reload (model ${rememberedModel} remembered)`);

// 4. 建立專案 → 在專案頁直接開聊
await page.click('button[aria-label="新增專案"]');
await page.fill('input[name=projectName]', '行銷網站');
await page.press('input[name=projectName]', 'Enter');
await page.waitForURL(/\/projects\//);
await page.waitForSelector('app-project-page h1:has-text("行銷網站")');
step('created project from the sidebar');

// 4b. 專案設定：專案 system prompt
await page.click('app-project-page details.settings summary');
await page.fill('textarea[name=projectPrompt]', '這是公司行銷網站，使用 Vue 3。');
await page.click('app-project-page details.settings button[type=submit]');
await page.waitForSelector('app-project-page [role=status]:has-text("已儲存")');
await page.screenshot({ path: `${outDir}/08-project-settings.png` });
step('project system prompt saved');
await sendPrompt('[create-file] 在專案裡建立檔案');
await page.waitForURL(/\/c\//);
await page.waitForSelector('.turn.assistant:has-text("已完成")', { timeout: 60000 });
await waitIdle();
await page.waitForSelector('.chat-header .crumb:has-text("行銷網站")');
await page.waitForSelector('.project .nested a.item.active:has-text("在專案裡建立檔案")');
step('chat inside the project (breadcrumb + nested in sidebar)');

// 5. 停止執行中的 Agent
await sendPrompt('[slow] 請慢慢講');
await page.waitForSelector('.turn.live app-markdown .markdown:not(:empty)');
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

// 6b. Agent 回覆以 Markdown 排版（標題、程式碼區塊 + 複製、表格），不顯示原始語法
await page.context().grantPermissions(['clipboard-read', 'clipboard-write']);
await sendPrompt('[markdown] 給我指令');
await page.waitForSelector('.turn.live app-markdown pre code'); // 串流中（含未閉合的 code fence）就排版
await waitIdle();
const reply = page.locator('.turn.assistant:not(.live) app-markdown').last();
await reply.locator('h2:has-text("啟用本機管理員帳戶")').waitFor();
await reply
  .locator('pre code.language-cmd:has-text("net user Administrator /active:yes")')
  .waitFor();
await reply.locator('table td code:has-text("/active:no")').waitFor();
const replyText = await reply.innerText();
if (replyText.includes('##') || replyText.includes('```'))
  throw new Error(`raw markdown shown: ${replyText}`);
await reply.locator('pre .code-copy').click();
await reply.locator('pre .code-copy:has-text("已複製")').waitFor();
const copied = await page.evaluate(() => navigator.clipboard.readText());
if (copied.trim() !== 'net user Administrator /active:yes')
  throw new Error(`unexpected clipboard: ${copied}`);
await page.screenshot({ path: `${outDir}/09-markdown.png` });
await page.emulateMedia({ colorScheme: 'dark' });
await page.screenshot({ path: `${outDir}/10-markdown-dark.png` });
await page.emulateMedia({ colorScheme: 'light' });
step('assistant markdown rendered (heading, code block with copy, table)');

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
