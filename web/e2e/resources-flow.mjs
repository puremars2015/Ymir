// 系統資源的端對端驗證（ADR-0025）：主機資源、Ymir 的 container / image、回收政策、dry run、回收紀錄、權限與窄螢幕。
// 前置：SQL Server；主機維護服務（Ymir.MaintenanceHost，例如以目前帳號的 rootless store 在 loopback 執行）；
//       API 設定 Ymir__Maintenance__Endpoint / Ymir__Maintenance__Token 指向它；ng serve 或 API 提供 Angular build。
// 本流程只查詢與 dry run，不會刪除任何 container 或 image。
// 用法：node e2e/resources-flow.mjs <screenshot-dir> [baseUrl]
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

// 1. Admin 從分頁進入系統資源：主機資源、store 狀態、container 與 image 表格
const admin = await (
  await browser.newContext({ viewport: { width: 1280, height: 900 } })
).newPage();
await devLogin(admin, `res-admin-${stamp}`, 'Admin');
await admin.goto(`${baseUrl}/admin`);
await admin.click('app-admin-tabs a:has-text("系統資源")');
await admin.waitForURL(/\/admin\/resources$/);
await admin.waitForSelector('app-resources-page h2:has-text("主機資源")', { timeout: 30000 });
const cpu = await admin.locator('.stat:has-text("CPU")').innerText();
const memory = await admin.locator('.stat:has-text("記憶體可用")').innerText();
if (!/\d/.test(memory)) throw new Error(`memory stat has no value: ${memory}`);
if ((await admin.locator('.stat:has-text("磁碟")').count()) === 0)
  throw new Error('expected at least one filesystem');
step(`host resources shown (${cpu.replace(/\s+/g, ' ')})`);

const containerRows = await admin.locator('article:has(h2:has-text("Containers")) tbody tr').count();
const imageRows = await admin.locator('article:has(h2:has-text("Images")) tbody tr').count();
if (containerRows === 0 || imageRows === 0) throw new Error('container / image tables are empty');
const body = await admin.locator('app-resources-page').innerText();
if (/\/home\/|\/var\/lib|\/srv\//.test(body)) throw new Error('page must not show host paths');
step(`containers (${containerRows}) and images (${imageRows}) listed without host paths`);
await admin.screenshot({ path: `${outDir}/resources-overview.png`, fullPage: true });

// 2. 政策：不合法的值不能儲存；合法的值儲存並顯示摘要
const policy = admin.locator('article:has(h2:has-text("自動回收政策"))');
await policy.locator('input[name=imageRetentionDays]').fill('-1');
await policy.locator('text=保留天數必須是').waitFor();
if (!(await policy.locator('button[type=submit]').isDisabled()))
  throw new Error('invalid policy must not be savable');
await policy.locator('input[name=imageRetentionDays]').fill('30');
await policy.locator('input[name=diskMinAvailablePercent]').fill('10');
await policy.locator('input[name=enabled]').check();
await policy.locator('button[type=submit]').click();
await policy.locator('[role=status]:has-text("已儲存")').waitFor();
await policy.locator('text=未使用的 image 30 天後回收').waitFor();
step('policy validated, saved and summarised');

// 3. Dry run：列出候選但不刪除
await policy.locator('button:has-text("Dry run")').click();
await policy.locator('h3:has-text("Dry run 結果")').waitFor({ timeout: 60000 });
step('dry run shows candidates and notes');
await admin.screenshot({ path: `${outDir}/resources-policy.png`, fullPage: true });

// 4. 回收紀錄卡片
await admin.locator('h2:has-text("回收紀錄")').waitFor();
step('reclaim run history shown');

// 5. 一般使用者看不到系統資源
const user = await (await browser.newContext()).newPage();
await devLogin(user, `res-user-${stamp}`, 'User');
await user.goto(`${baseUrl}/admin/resources`);
await user.waitForTimeout(1000);
if ((await user.locator('app-resources-page h1').count()) !== 0)
  throw new Error('regular user must not see the resources page');
step('regular user cannot open the resources page');

// 6. 窄螢幕：表格可水平捲動，頁面本身不水平捲動
const mobile = await (
  await browser.newContext({ viewport: { width: 390, height: 844 } })
).newPage();
await devLogin(mobile, `res-mobile-${stamp}`, 'Admin');
await mobile.goto(`${baseUrl}/admin/resources`);
await mobile.waitForSelector('app-resources-page h2:has-text("主機資源")', { timeout: 30000 });
const overflow = await mobile.evaluate(
  () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
);
if (overflow > 1) throw new Error(`page scrolls horizontally on mobile (${overflow}px)`);
await mobile.screenshot({ path: `${outDir}/resources-mobile.png`, fullPage: true });
step('mobile layout has no horizontal page scroll');

await browser.close();
