// 登入方式的端對端驗證（ADR-0009）：企業帳號（Fake OIDC 模擬 Entra ID）、本機帳號、Admin 使用者管理、停用後立即登出。
// 前置：SQL Server、Fake OIDC（dotnet run --project tests/Ymir.Testing.FakeOidc）、
//       API 設定 Ymir__Auth__Oidc__Authority / ClientId / ClientSecret 指向 Fake OIDC。
// 用法：node e2e/auth-flow.mjs <screenshot-dir> [baseUrl]
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const executablePath = process.env.CHROMIUM_PATH;
const browser = await chromium.launch(executablePath ? { executablePath } : {});
const step = (name) => console.log(`✔ ${name}`);
const stamp = Date.now();
const localAccount = `local-${stamp}`;
const initialPassword = 'initial-password-123';
const newPassword = 'my-own-password-456';

// 1. 登入頁：企業帳號按鈕 + 帳號密碼
const admin = await (await browser.newContext({ viewport: { width: 1280, height: 800 } })).newPage();
await admin.goto(`${baseUrl}/login`);
await admin.waitForSelector('a.sso');
await admin.screenshot({ path: `${outDir}/01-login.png` });
step('login page shows company-account button and password form');

// 2. 以公司帳號登入（Fake Entra ID 的登入頁），帶 Ymir.Admin 角色
await admin.click('a.sso');
await admin.waitForSelector('input[name=account]');
await admin.fill('input[name=account]', `admin-${stamp}`);
await admin.fill('input[name=display_name]', '管理員 Sean');
await admin.check('input[name=admin]');
await admin.screenshot({ path: `${outDir}/02-fake-entra.png` });
await admin.click('button[type=submit]');
await admin.waitForSelector('app-new-chat-page h1');
await admin.waitForSelector('.user .name:has-text("管理員 Sean")');
step('signed in with the company account (OIDC) and returned to the app');

// 3. 使用者管理：建立本機帳號
await admin.click('.user a:has-text("管理")');
await admin.waitForSelector('app-users-page table');
await admin.click('app-users-page header button');
await admin.fill('input[name=account]', localAccount);
await admin.fill('input[name=displayName]', '外部顧問 Amy');
await admin.fill('input[name=password]', initialPassword);
await admin.click('form.create button[type=submit]');
await admin.waitForSelector(`app-users-page [role=status]:has-text("${localAccount}")`);
await admin.screenshot({ path: `${outDir}/03-admin-users.png` });
step('admin created a local account');

// 4. 本機帳號第一次登入 → 必須改密碼 → 進主畫面
const local = await (await browser.newContext({ viewport: { width: 1280, height: 800 } })).newPage();
await local.goto(`${baseUrl}/login`);
await local.fill('input[name=account]', localAccount);
await local.fill('input[name=password]', initialPassword);
await local.click('form button[type=submit]:has-text("登入")');
await local.waitForURL(/\/change-password/);
await local.fill('input[name=current]', initialPassword);
await local.fill('input[name=next]', newPassword);
await local.fill('input[name=confirm]', newPassword);
await local.screenshot({ path: `${outDir}/04-change-password.png` });
await local.click('button[type=submit]:has-text("變更密碼")');
await local.waitForSelector('app-new-chat-page h1');
step('local account had to change the initial password, then entered the app');

// 5. Admin 停用本機帳號 → 對方下一個請求就被登出
admin.on('dialog', (dialog) => void dialog.accept());
await admin.reload();
const row = admin.locator('tr', { hasText: localAccount });
await row.locator('button:has-text("停用")').click();
await row.locator('.danger-text:has-text("已停用")').waitFor();
await admin.screenshot({ path: `${outDir}/05-disabled.png` });
await local.click('.user a:has-text("設定")');
await local.waitForURL(/\/login/);
step('disabled user was signed out on the next request');

// 6. 停用的帳號不能再登入；重新啟用後可以
await local.fill('input[name=account]', localAccount);
await local.fill('input[name=password]', newPassword);
await local.click('form button[type=submit]:has-text("登入")');
await local.waitForSelector('.error:has-text("停用")');
await row.locator('button:has-text("啟用")').click();
await row.locator('td:has-text("啟用中")').waitFor();
await local.fill('input[name=password]', newPassword);
await local.click('form button[type=submit]:has-text("登入")');
// 被登出時登入頁記住了原本要去的設定頁（returnUrl）
await local.waitForSelector('app-settings-page h1');
step('disabled account cannot sign in; re-enabled account can (back to where it was)');

console.log(`admin: admin-${stamp}, local: ${localAccount}`);
await browser.close();
