// 主要使用流程的端對端驗證（手動執行，尚未進 CI）：ChatGPT 式版面、直接開聊、專案（ADR-0007）。
// 前置：SQL Server、Fake LLM、API（VibeMaker__Harness=Pi）、ng serve 都已啟動。
// 用法：node e2e/chat-flow.mjs <screenshot-dir> [baseUrl]
import { chromium } from 'playwright';
import { mkdirSync, readFileSync } from 'node:fs';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const executablePath = process.env.CHROMIUM_PATH;
const browser = await chromium.launch(executablePath ? { executablePath } : {});
const page = await browser.newPage({
  viewport: { width: 1280, height: 800 },
  acceptDownloads: true,
});
const step = (name) => console.log(`✔ ${name}`);
page.on('dialog', (dialog) => void dialog.accept()); // 刪除前的確認
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

// 2b. Agent 建立的檔案可以下載：回覆下方出現檔案、檔案面板、打包下載
const chip = page.locator('.turn-files a.chip:has-text("hello.txt")');
await chip.waitFor();
const [fileDownload] = await Promise.all([page.waitForEvent('download'), chip.click()]);
if (fileDownload.suggestedFilename() !== 'hello.txt')
  throw new Error(`unexpected file name: ${fileDownload.suggestedFilename()}`);
const fileContent = readFileSync(await fileDownload.path(), 'utf8');
if (fileContent !== 'Hello from Ymir') throw new Error(`unexpected content: ${fileContent}`);
await page.click('button.files-toggle');
await page.waitForSelector('app-files-panel li.file:has-text("hello.txt")');
await page.screenshot({ path: `${outDir}/02b-files.png` });
// 點檔名預覽：文字以原始碼顯示
await page.click('app-files-panel button.open:has-text("hello.txt")');
await page.waitForSelector('app-files-panel pre.code:has-text("Hello from Ymir")');
await page.screenshot({ path: `${outDir}/02c-file-preview.png` });
await page.click('app-files-panel .preview-head button:has-text("返回")');
const [zipDownload] = await Promise.all([
  page.waitForEvent('download'),
  page.click('app-files-panel a.archive'),
]);
// headless Chromium 無法處理非 ASCII 檔名（會改叫 "download"），所以直接檢查 header：一般瀏覽器用 filename* 取得中文檔名
const archiveResponse = await page.request.get(zipDownload.url());
const disposition = archiveResponse.headers()['content-disposition'] ?? '';
if (!disposition.startsWith('attachment') || !/filename\*=UTF-8''.+\.zip/.test(disposition))
  throw new Error(`unexpected archive disposition: ${disposition}`);
const zipBytes = readFileSync(await zipDownload.path());
if (zipBytes[0] !== 0x50 || zipBytes[1] !== 0x4b) throw new Error('archive is not a zip file');
await page.click('app-files-panel button.close');
step('agent-created file previewable and downloadable (inline chip, files panel, zip)');

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

// 5. 執行中重新整理頁面 → 自動接回串流（不重複顯示使用者訊息）→ 停止
await sendPrompt('[slow] 請慢慢講');
await page.waitForSelector('.turn.live app-markdown .markdown:not(:empty)');
await page.reload();
await page.waitForSelector('.turn.live app-markdown .markdown:not(:empty)', { timeout: 15000 });
const slowPrompts = await page.locator('.turn.user:has-text("[slow] 請慢慢講")').count();
if (slowPrompts !== 1) throw new Error(`resumed turn shows the prompt ${slowPrompts} times`);
await page.screenshot({ path: `${outDir}/03a-resumed.png` });
await page.click('button[aria-label="停止"]');
await waitIdle();
await page.waitForSelector('button[aria-label="送出"]');
step('re-attached to the running execution after reload, then cancelled');

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

// 6c. 複製整則回覆（Markdown 原文）
await page.locator('.turn.assistant:not(.live) .copy-reply').last().click();
await page.locator('.turn.assistant:not(.live) .copy-reply:has-text("已複製")').last().waitFor();
const copiedReply = await page.evaluate(() => navigator.clipboard.readText());
if (!copiedReply.includes('## 啟用本機管理員帳戶'))
  throw new Error(`reply copy is not the raw markdown: ${copiedReply}`);
step('copied a whole reply as markdown');

// 6d. 標題點兩下改名，側邊欄同步
await page.dblclick('.chat-header .title');
await page.fill('.chat-header .title-input', '管理員指令');
await page.press('.chat-header .title-input', 'Enter');
await page.waitForSelector('.chat-header .title:has-text("管理員指令")');
await page.waitForSelector('.project .nested a.item.active:has-text("管理員指令")');
await page.reload();
await page.waitForSelector('.chat-header .title:has-text("管理員指令")');
step('renamed the conversation from the chat title');

// 7. 專案頁列出對話；側邊欄切換對話
await page.click('.chat-header .crumb');
await page.waitForSelector('app-project-page .conversations a:has-text("管理員指令")');
await page.screenshot({ path: `${outDir}/04-project.png` });
await page.click('.sidebar .section:has-text("聊天") a.item:has-text("幫我建立一個檔案")');
await page.waitForSelector('.turn.assistant:has-text("已完成")');
step('project page lists its chats; switching chats from the sidebar');

// 7b. 側邊欄「⋯」選單：專案改名
const projectRow = page.locator('.sidebar .project .row.has-menu').first();
await projectRow.hover();
await projectRow.locator('button.more').click();
await page.waitForSelector('.sidebar .menu');
await page.screenshot({ path: `${outDir}/11-item-menu.png` });
await page.click('.sidebar .menu button:has-text("重新命名")');
await page.fill('.sidebar input.rename', '行銷網站 2026');
await page.press('.sidebar input.rename', 'Enter');
await page.waitForSelector('.sidebar .project a.item:has-text("行銷網站 2026")');
step('renamed the project from the sidebar menu');

// 7c. 刪除目前開啟的對話 → 從側邊欄消失並回到首頁
const chatRow = page.locator('.sidebar .section:has-text("聊天") .row.has-menu').first();
await chatRow.hover();
await chatRow.locator('button.more').click();
await page.click('.sidebar .menu button:has-text("刪除")');
await page.waitForSelector('app-new-chat-page h1');
if ((await page.locator('.sidebar a.item:has-text("幫我建立一個檔案")').count()) !== 0)
  throw new Error('archived conversation still in the sidebar');
step('deleted (archived) the open conversation');

// 7d. 刪除專案（連同專案內的對話）
await projectRow.hover();
await projectRow.locator('button.more').click();
await page.click('.sidebar .menu button:has-text("刪除")');
await page.waitForSelector('.sidebar .project', { state: 'detached' });
await page.reload();
await page.waitForSelector('.sidebar .section-header:has-text("專案")');
if ((await page.locator('.sidebar .project').count()) !== 0)
  throw new Error('archived project still listed after reload');
step('deleted (archived) the project and its chats');

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
