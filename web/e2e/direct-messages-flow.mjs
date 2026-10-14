// 使用者之間的訊息（ADR-0023）端對端驗證：右側成員名單、未讀數、即時收發、已讀、窄螢幕抽屜。
// 前置：SQL Server、API（Development，Scripted harness 即可）、ng serve 都已啟動。
// 用法：node e2e/direct-messages-flow.mjs <screenshot-dir> [baseUrl]
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';
import assert from 'node:assert/strict';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const executablePath = process.env.CHROMIUM_PATH;
const browser = await chromium.launch(executablePath ? { executablePath } : {});
const step = (name) => console.log(`✔ ${name}`);
const suffix = Date.now().toString(36);

async function login(account, displayName, viewport = { width: 1440, height: 900 }) {
  const context = await browser.newContext({ viewport });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await page.goto(`${baseUrl}/login`);
  await page.waitForSelector('details.dev');
  if (!(await page.locator('details.dev').evaluate((d) => d.open))) {
    await page.click('details.dev summary');
  }
  await page.fill('input[name=devAccount]', account);
  if (await page.locator('input[name=devDisplayName]').count()) {
    await page.fill('input[name=devDisplayName]', displayName);
  }
  await page.click('details.dev button[type=submit]');
  await page.waitForSelector('app-new-chat-page h1');
  return { context, page, errors };
}

const alice = await login(`dm-alice-${suffix}`, `Alice ${suffix}`);
const bob = await login(`dm-bob-${suffix}`, `Bob ${suffix}`);
// Alice 登入時 Bob 還不存在：重新整理讓名單出現 Bob
await alice.page.reload();
const bobName = await bob.page.locator('.user .name').innerText();
const aliceName = await alice.page.locator('.user .name').innerText();

// 1. 桌面版右側預設顯示成員名單，並已連上即時通知
await alice.page.locator('app-people-panel .status.online').waitFor();
await alice.page.fill('app-people-panel input[type=search]', bobName);
await alice.page.locator('app-people-panel .contact', { hasText: bobName }).waitFor();
await alice.page.screenshot({ path: `${outDir}/01-people-list.png` });
step('desktop: people panel lists other users');

// 2. Alice 開啟與 Bob 的對話並送出訊息（Enter 送出）
await bob.page.locator('app-people-panel .status.online').waitFor();
await alice.page.click(`app-people-panel .contact:has-text("${bobName}")`);
await alice.page.fill('app-people-panel textarea', '嗨 Bob，下午三點開會可以嗎？');
await alice.page.press('app-people-panel textarea', 'Enter');
await alice.page.locator('app-people-panel .message.mine .bubble', { hasText: '下午三點' }).waitFor();
step('alice sends a message');

// 3. Bob 即時看到未讀數（成員按鈕與名單）
await bob.page.locator('.people-badge', { hasText: '1' }).waitFor();
await bob.page.fill('app-people-panel input[type=search]', aliceName);
await bob.page.locator(`app-people-panel .contact:has-text("${aliceName}") .badge`, { hasText: '1' }).waitFor();
await bob.page.screenshot({ path: `${outDir}/02-unread.png` });
step('bob receives unread badge in real time');

// 4. Bob 開啟對話：未讀歸零，Alice 看到「已讀」；Bob 回覆，Alice 即時收到
await bob.page.click(`app-people-panel .contact:has-text("${aliceName}")`);
await bob.page.locator('app-people-panel .message:not(.mine) .bubble', { hasText: '下午三點' }).waitFor();
await bob.page.locator('.people-badge').waitFor({ state: 'detached' });
await alice.page.locator('app-people-panel .message.mine .meta', { hasText: '已讀' }).waitFor();
await bob.page.fill('app-people-panel textarea', '可以！\n我會準備簡報。');
await bob.page.press('app-people-panel textarea', 'Enter');
await alice.page.locator('app-people-panel .message:not(.mine) .bubble', { hasText: '我會準備簡報' }).waitFor();
// 訊息以文字顯示：HTML 不會被解譯
await alice.page.fill('app-people-panel textarea', '<b>不是粗體</b>');
await alice.page.click('app-people-panel button.send');
await bob.page.locator('app-people-panel .bubble', { hasText: '<b>不是粗體</b>' }).waitFor();
assert.equal(await bob.page.locator('app-people-panel .bubble b').count(), 0);
await alice.page.screenshot({ path: `${outDir}/03-chat-alice.png` });
await bob.page.screenshot({ path: `${outDir}/04-chat-bob.png` });
step('read receipt, reply and plain-text rendering');

// 5. 收合面板後記住狀態；新訊息只顯示在成員按鈕的未讀數
await bob.page.click('.people-toggle');
await bob.page.waitForFunction(() => document.querySelector('.people-toggle')?.getAttribute('aria-expanded') === 'false');
await alice.page.fill('app-people-panel textarea', '收到了嗎？');
await alice.page.press('app-people-panel textarea', 'Enter');
await bob.page.locator('.people-badge', { hasText: '1' }).waitFor();
await bob.page.reload();
await bob.page.waitForFunction(() => document.querySelector('.people-toggle')?.getAttribute('aria-expanded') === 'false');
await bob.page.click('.people-toggle');
await bob.page.waitForFunction(() => document.querySelector('.people-toggle')?.getAttribute('aria-expanded') === 'true');
step('collapsed panel keeps unread count');

// 6. 窄螢幕：成員面板是右側抽屜
const mobile = await login(`dm-carol-${suffix}`, `Carol ${suffix}`, { width: 390, height: 844 });
assert.equal(await mobile.page.locator('app-people-panel').evaluate((el) => el.inert), true);
await mobile.page.click('.people-toggle');
await mobile.page.locator('app-people-panel .contact').first().waitFor();
await mobile.page.waitForTimeout(300);
await mobile.page.screenshot({ path: `${outDir}/05-mobile-drawer.png` });
await mobile.page.click('app-people-panel .contact >> nth=0');
await mobile.page.locator('app-people-panel textarea').waitFor();
await mobile.page.screenshot({ path: `${outDir}/06-mobile-chat.png` });
await mobile.page.keyboard.press('Escape');
await mobile.page.waitForFunction(() => document.querySelector('.people-toggle')?.getAttribute('aria-expanded') === 'false');
step('mobile drawer');

for (const { errors } of [alice, bob, mobile]) assert.deepEqual(errors, []);
await browser.close();
