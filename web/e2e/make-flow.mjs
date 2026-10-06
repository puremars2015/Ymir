// /make 指令與 Make 主題管理的端對端驗證（手動執行，尚未進 CI）。
// 前置：SQL Server、Fake LLM、API（VibeMaker__Harness=Pi，Development）、ng serve 都已啟動。
// 用法：node e2e/make-flow.mjs <screenshot-dir> [baseUrl]
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const executablePath = process.env.CHROMIUM_PATH;
const browser = await chromium.launch(executablePath ? { executablePath } : {});
const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
page.on('dialog', (d) => d.accept()); // 刪除主題的確認對話框
const step = (name) => console.log(`✔ ${name}`);
const composer = 'app-composer textarea';
const waitIdle = () => page.waitForSelector('.turn.live', { state: 'detached', timeout: 60000 });
const topicButtons = 'app-composer .make-picker button.topic';

// 1. Dev 登入（Admin，之後要管理主題）
await page.goto(`${baseUrl}/login`);
await page.waitForSelector('details.dev');
if (!(await page.locator('details.dev').evaluate((d) => d.open))) {
  await page.click('details.dev summary');
}
await page.fill('input[name=devAccount]', `make-${Date.now()}`);
await page.selectOption('details.dev select[name=role]', 'Admin');
await page.click('details.dev button[type=submit]');
await page.waitForSelector('app-new-chat-page h1');
step('dev login as Admin');

// 2. 輸入 "/" 出現 /make 提示，點提示填入 "/make "
await page.fill(composer, '/');
await page.waitForSelector('app-composer .make-hint');
await page.screenshot({ path: `${outDir}/01-make-hint.png` });
await page.click('app-composer .make-hint');
await page.waitForFunction(
  (sel) => document.querySelector(sel)?.value === '/make ',
  composer,
  { timeout: 5000 },
);
step('typing "/" shows the /make hint');

// 3. 只送出 /make → 顯示主題按鈕（預設兩個），不送給 Agent
await page.press(composer, 'Enter');
await page.waitForSelector(topicButtons);
const names = await page.locator(`${topicButtons} strong`).allInnerTexts();
for (const expected of ['小工具架設', '網站系統架設']) {
  if (!names.includes(expected)) throw new Error(`missing topic ${expected}: ${names}`);
}
if (page.url().includes('/c/')) throw new Error('bare /make must not start a chat');
await page.screenshot({ path: `${outDir}/02-make-topics.png` });
step(`bare /make shows topic buttons: ${names.join('、')}`);

// 4. 點「小工具架設」→ 直接送出，對話顯示短文字並收到回覆
await page.click(`${topicButtons}:has-text("小工具架設")`);
await page.waitForURL(/\/c\//);
await waitIdle();
await page.waitForSelector('.turn.user:has-text("/make 小工具架設")');
await page.waitForSelector('.turn.assistant:not(.live)');
await page.waitForSelector('.sidebar a.item.active:has-text("小工具架設")');
await page.screenshot({ path: `${outDir}/03-make-topic-chat.png` });
step('topic button sends "/make 小工具架設" (title without /make)');

// 5. /make <描述> 照常送出並收到回覆
const before = await page.locator('.turn.assistant:not(.live)').count();
await page.fill(composer, '/make 一個計算機');
await page.press(composer, 'Enter');
await page.waitForFunction(
  (n) =>
    document.querySelectorAll('.turn.assistant:not(.live)').length > n &&
    !document.querySelector('.turn.live'),
  before,
  { timeout: 60000 },
);
await page.waitForSelector('.turn.user:has-text("/make 一個計算機")');
step('"/make 一個計算機" sent and answered');

// 6. Admin 新增主題 → 對話輸入框出現第三個按鈕
const topicName = `資料報表-${Date.now() % 100000}`;
await page.click('.user a:has-text("管理")');
await page.waitForURL(/\/admin\/users/);
await page.click('app-admin-tabs a:has-text("Make 主題")');
await page.waitForURL(/\/admin\/make-topics/);
await page.waitForSelector('app-make-topics-page li.topic:has-text("小工具架設")');
await page.click('app-make-topics-page button:has-text("新增主題")');
await page.fill('input[name=name]', topicName);
await page.fill('input[name=description]', 'Excel 匯入、圖表與報表');
await page.fill('textarea[name=instructions]', '先確認資料來源與欄位，再做成單頁報表。');
await page.click('app-make-topics-page form button[type=submit]');
await page.waitForSelector(`app-make-topics-page li.topic:has-text("${topicName}")`);
await page.screenshot({ path: `${outDir}/04-admin-make-topics.png` });
step(`admin created topic ${topicName}`);

// 6b. 上移：新主題排到第二個
const topicItem = `app-make-topics-page li.topic:has-text("${topicName}")`;
await page.click(`${topicItem} button[aria-label="上移"]`);
await page.waitForFunction(
  (name) =>
    document.querySelectorAll('app-make-topics-page li.topic strong')[1]?.textContent === name,
  topicName,
);
step('moved the new topic up');

const openPicker = async () => {
  await page.click('a.new-chat');
  await page.waitForSelector('app-new-chat-page h1');
  await page.fill(composer, '/make');
  await page.press(composer, 'Enter');
  await page.waitForSelector(topicButtons);
  return page.locator(`${topicButtons} strong`).allInnerTexts();
};
let picked = await openPicker();
if (picked[1] !== topicName) throw new Error(`new topic not shown second: ${picked}`);
step(`composer shows the new topic (${picked.join('、')})`);

// 7. 停用後按鈕消失；最後刪除測試主題
await page.click('.user a:has-text("管理")');
await page.click('app-admin-tabs a:has-text("Make 主題")');
await page.click(`${topicItem} button:has-text("停用")`);
await page.waitForSelector(`${topicItem} .badge:has-text("已停用")`);
picked = await openPicker();
if (picked.includes(topicName)) throw new Error('disabled topic still shown');
step('disabled topic disappears from the composer');

await page.click('.user a:has-text("管理")');
await page.click('app-admin-tabs a:has-text("Make 主題")');
await page.click(`${topicItem} button:has-text("刪除")`);
await page.waitForSelector(topicItem, { state: 'detached' });
step('deleted the test topic');

await browser.close();
