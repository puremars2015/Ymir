// Google 帳號 SSO 的端對端驗證（ADR-0024）：Fake OIDC 的 Google 模擬、白名單內的 Workspace 帳號登入、白名單外的帳號被拒絕。
// 前置：SQL Server、Fake OIDC（dotnet run --project tests/Ymir.Testing.FakeOidc）、
//       API 設定 Ymir__Auth__Google__Authority=http://127.0.0.1:5299/google、ClientId=ymir-dev、ClientSecret=ymir-dev-secret、
//       AllowedDomains__0=acme.test（見 docs/guides/google-login.md 第 3 節）。
// 用法：node e2e/google-flow.mjs <screenshot-dir> [baseUrl]
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const executablePath = process.env.CHROMIUM_PATH;
const browser = await chromium.launch(executablePath ? { executablePath } : {});
const step = (name) => console.log(`✔ ${name}`);
const stamp = Date.now();

// 1. 登入頁：Google 帳號按鈕
const page = await (await browser.newContext({ viewport: { width: 1280, height: 800 } })).newPage();
await page.goto(`${baseUrl}/login`);
await page.waitForSelector('a.sso.google');
await page.screenshot({ path: `${outDir}/01-login-google.png` });
step('login page shows the Google button');

// 2. 白名單內的 Workspace 帳號（acme.test 帶 hd）→ 回到 Ymir
await page.click('a.sso.google');
await page.waitForSelector('input[name=email]');
await page.fill('input[name=email]', `alice-${stamp}@acme.test`);
await page.fill('input[name=display_name]', 'Alice Google');
await page.screenshot({ path: `${outDir}/02-fake-google.png` });
await page.click('button[type=submit]');
await page.waitForSelector('app-new-chat-page h1');
await page.waitForSelector('.user .name:has-text("Alice Google")');
await page.screenshot({ path: `${outDir}/03-signed-in.png` });
step('signed in with an allowed Google Workspace account');

// 3. 白名單外的個人 Gmail → 登入頁顯示未獲准
const stranger = await (
  await browser.newContext({ viewport: { width: 390, height: 844 } })
).newPage();
await stranger.goto(`${baseUrl}/login`);
await stranger.click('a.sso.google');
await stranger.waitForSelector('input[name=email]');
await stranger.fill('input[name=email]', `stranger-${stamp}@gmail.com`);
await stranger.click('button[type=submit]');
await stranger.waitForSelector('p.error:has-text("未獲准")');
await stranger.screenshot({ path: `${outDir}/04-not-allowed.png` });
step('an account outside the allow list is rejected');

await browser.close();
