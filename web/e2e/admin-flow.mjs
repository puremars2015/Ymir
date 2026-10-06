// 管理介面的端對端驗證（ADR-0010）：總覽、停止執行環境、稽核紀錄。
// 前置：SQL Server、Fake LLM、API（VibeMaker__Harness=Pi，Development）、ng serve 都已啟動。
// 用法：node e2e/admin-flow.mjs <screenshot-dir> [baseUrl]
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const executablePath = process.env.CHROMIUM_PATH;
const browser = await chromium.launch(executablePath ? { executablePath } : {});
const step = (name) => console.log(`✔ ${name}`);
const stamp = Date.now();

const devLogin = async (page, account, role) => {
  await page.goto(`${baseUrl}/login`);
  await page.waitForSelector('details.dev');
  if (!(await page.locator('details.dev').evaluate((d) => d.open))) {
    await page.click('details.dev summary');
  }
  await page.fill('input[name=devAccount]', account);
  await page.selectOption('details.dev select[name=role]', role);
  await page.click('details.dev button[type=submit]');
  await page.waitForSelector('app-new-chat-page h1');
};

// 1. 一般使用者送一則訊息，建立自己的執行環境
const worker = await (await browser.newContext()).newPage();
const workerAccount = `worker-${stamp}`;
await devLogin(worker, workerAccount, 'User');
await worker.fill('app-composer textarea', '[create-file] 管理介面測試');
await worker.press('app-composer textarea', 'Enter');
await worker.waitForURL(/\/c\//);
await worker.waitForSelector('.turn.assistant:has-text("已完成")', { timeout: 60000 });
await worker.waitForSelector('.turn.live', { state: 'detached', timeout: 60000 });
if ((await worker.locator('.user a:has-text("管理")').count()) !== 0)
  throw new Error('regular user must not see the admin link');
step('regular user ran the agent (runtime created, no admin link)');

// 2. Admin 進入總覽：統計卡片、近 7 天長條圖、執行環境清單
const admin = await (
  await browser.newContext({ viewport: { width: 1280, height: 900 } })
).newPage();
admin.on('dialog', (d) => d.accept());
await devLogin(admin, `admin-${stamp}`, 'Admin');
await admin.click('.user a:has-text("管理")');
await admin.waitForURL(/\/admin$/);
await admin.waitForSelector('app-overview-page .stat:has-text("今日執行")');
const bars = await admin.locator('app-overview-page .bar-col').count();
if (bars !== 7) throw new Error(`expected 7 trend bars, got ${bars}`);
const row = admin.locator(`app-overview-page tr:has-text("${workerAccount}")`);
await row.waitFor();
await admin.screenshot({ path: `${outDir}/01-overview.png`, fullPage: true });
step('overview shows stats, 7-day trend and the worker runtime');

// 3. 停止使用者的執行環境
await row.locator('button:has-text("停止")').click();
await admin.waitForSelector('app-overview-page [role=status]:has-text("已停止")');
await row.locator('.badge:has-text("已停止")').waitFor();
if ((await row.locator('button:has-text("停止")').count()) !== 0)
  throw new Error('stopped runtime still offers stop');
step('stopped the worker runtime');

// 4. 從總覽跳到該使用者的稽核紀錄，看到剛才的停止操作與使用者的登入
await row.locator('a:has-text("稽核紀錄")').click();
await admin.waitForURL(/\/admin\/audit\?/);
await admin.waitForSelector(`app-audit-page .chip:has-text("${workerAccount}")`);
await admin.waitForSelector('app-audit-page tr:has-text("停止執行環境")');
await admin.waitForSelector('app-audit-page tr:has-text("開發登入")');
await admin.waitForSelector('app-audit-page tr:has-text("送出訊息")');
await admin.screenshot({ path: `${outDir}/02-audit-user.png`, fullPage: true });
step('audit page filtered by the worker shows stop, login and executions');

// 5. 篩選「管理操作」：只剩 admin.* 紀錄
await admin.selectOption('app-audit-page select[name=action]', 'admin.');
await admin.click('app-audit-page form button[type=submit]');
await admin.waitForFunction(() => {
  const rows = [...document.querySelectorAll('app-audit-page tbody tr code')];
  return rows.length > 0 && rows.every((c) => c.textContent.startsWith('admin.'));
});
step('action filter narrows to admin operations');

// 6. 清除使用者篩選後看到所有人的紀錄；窄螢幕截圖
await admin.click('app-audit-page .chip button');
await admin.waitForSelector('app-audit-page .chip', { state: 'detached' });
await admin.waitForSelector('app-audit-page tbody tr code');
await admin.setViewportSize({ width: 390, height: 800 });
await admin.click('app-admin-tabs a:has-text("總覽")');
await admin.waitForSelector('app-overview-page .stat');
await admin.screenshot({ path: `${outDir}/03-overview-mobile.png`, fullPage: true });
step('cleared the user filter; overview on a narrow screen');

await browser.close();
