// OneDrive connector 的端對端驗證（ADR-0013）：管理員開放 → 使用者在個人設定連結（經 Fake OIDC 授權）→ 設定同步資料夾 → 解除連結。
// 前置：SQL Server、Fake OIDC（dotnet run --project tests/Ymir.Testing.FakeOidc，含 Fake Graph）；
//       API 設定 Ymir__Auth__Oidc__Authority=http://127.0.0.1:5299/00000000-0000-0000-0000-00000000f00d/v2.0、
//       Ymir__Auth__Oidc__ClientId=ymir-dev、Ymir__Auth__Oidc__ClientSecret=ymir-dev-secret、
//       Ymir__Connectors__OneDrive__GraphBaseUrl=http://127.0.0.1:5299/graph/v1.0；ng serve。
// 用法：node e2e/onedrive-flow.mjs <screenshot-dir> [baseUrl]
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const browser = await chromium.launch(
  process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {},
);
const step = (name) => console.log(`✔ ${name}`);
const stamp = Date.now();

const devLogin = async (page, account, role) => {
  await page.goto(`${baseUrl}/login`);
  await page.waitForSelector('details.dev');
  if (!(await page.locator('details.dev').evaluate((d) => d.open)))
    await page.click('details.dev summary');
  await page.fill('input[name=devAccount]', account);
  await page.selectOption('details.dev select[name=role]', role);
  await page.click('details.dev button[type=submit]');
  await page.waitForSelector('app-new-chat-page h1');
};

// 1. 管理員開放 OneDrive（全域預設）
const admin = await (
  await browser.newContext({ viewport: { width: 1280, height: 900 } })
).newPage();
await devLogin(admin, `od-admin-${stamp}`, 'Admin');
await admin.goto(`${baseUrl}/admin/settings`);
const card = admin.locator('app-extension-settings-card');
await card.locator('input[name=oneDrive]').check();
await card.locator('button:has-text("儲存")').click();
await card.locator('.summary:has-text("OneDrive 連結：允許")').waitFor();
await card.screenshot({ path: `${outDir}/01-admin-onedrive.png` });
step('admin enabled OneDrive for everyone');

// 2. 使用者連結 OneDrive（整頁導向 Fake OIDC，login_hint 自動通過）
const user = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
await devLogin(user, `od-user-${stamp}`, 'User');
await user.goto(`${baseUrl}/settings`);
const onedrive = user.locator('app-onedrive-card');
await onedrive.locator('text=尚未連結').waitFor();
await user.screenshot({ path: `${outDir}/02-not-connected.png`, fullPage: true });
await onedrive.locator('button:has-text("連結 OneDrive")').click();
await user.waitForURL(/\/settings/, { timeout: 30000 });
await onedrive.locator('[role=status]:has-text("已連結 OneDrive")').waitFor();
await onedrive.locator(`text=od-user-${stamp}@fake-entra.test`).waitFor();
if (user.url().includes('onedrive='))
  throw new Error('result query should be removed from the URL');
step('user connected OneDrive through the authorization redirect');

// 3. 設定同步資料夾
await onedrive.locator('input[name=rootPath]').fill('/Ymir/E2E');
await onedrive.locator('button:has-text("儲存")').click();
await onedrive.locator('text=已連結，同步到 /Ymir/E2E').waitFor();
await user.screenshot({ path: `${outDir}/03-connected.png`, fullPage: true });
step("sync folder created in the user's OneDrive");

// 4. 解除連結
user.once('dialog', (d) => d.accept());
await onedrive.locator('button:has-text("解除連結")').click();
await onedrive.locator('text=尚未連結').waitFor();
step('disconnected');

await admin.goto(`${baseUrl}/admin/settings`);
await card.locator('input[name=oneDrive]').uncheck();
await card.locator('button:has-text("儲存")').click();
await card.locator('.summary:has-text("OneDrive 連結：不允許")').waitFor();
await browser.close();
