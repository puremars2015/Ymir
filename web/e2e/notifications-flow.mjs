// 站內通知（ADR-0032 N1）端對端驗證：長任務完成後頂列鈴鐺即時出現未讀、另一個分頁同步、點選前往對話並標為已讀。
// 前置：SQL Server、API（Development，Scripted harness 即可，另設 VibeMaker__Notifications__LongExecutionSeconds=0
//       讓每次完成都通知）、ng serve 都已啟動。
// 用法：node e2e/notifications-flow.mjs <screenshot-dir> [baseUrl]
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';
import assert from 'node:assert/strict';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const executablePath = process.env.CHROMIUM_PATH;
const browser = await chromium.launch(executablePath ? { executablePath } : {});
const step = (name) => console.log(`✔ ${name}`);
const account = `notify-${Date.now().toString(36)}`;

const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
const errors = [];
const watch = (page) => page.on('pageerror', (error) => errors.push(error.message));

const work = await context.newPage();
watch(work);
await work.goto(`${baseUrl}/login`);
await work.waitForSelector('details.dev');
if (!(await work.locator('details.dev').evaluate((d) => d.open))) {
  await work.click('details.dev summary');
}
await work.fill('input[name=devAccount]', account);
await work.click('details.dev button[type=submit]');
await work.waitForSelector('app-new-chat-page h1');

// 第二個分頁也停在首頁：兩個分頁都不是正在看那個對話，都應該收到未讀通知。
const other = await context.newPage();
watch(other);
await other.goto(`${baseUrl}/`);
await other.waitForSelector('app-new-chat-page h1');
await other.locator('app-notification-bell button.bell').waitFor();
assert.equal(await other.locator('app-notification-bell .badge').count(), 0);
step('bell is shown with no unread notifications');

// 1. 經 API 在背景開始一個工作（沒有任何分頁在看那個對話）；完成通知即時送到兩個分頁
const conversationId = await other.evaluate(async () => {
  const token = document.cookie
    .split('; ')
    .find((c) => c.startsWith('XSRF-TOKEN='))
    ?.slice('XSRF-TOKEN='.length);
  const post = (url, body) =>
    fetch(url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-XSRF-TOKEN': decodeURIComponent(token ?? ''),
      },
      body: JSON.stringify(body),
    });
  const conversation = await (
    await post('/api/conversations', { projectId: null, title: '本週進度' })
  ).json();
  const sent = await post(`/api/conversations/${conversation.id}/messages`, {
    content: '幫我整理本週進度',
    clientRequestId: crypto.randomUUID(),
  });
  if (sent.status !== 202) throw new Error(`send failed: ${sent.status}`);
  return conversation.id;
});
const conversationPath = `/c/${conversationId}`;
await work.locator('app-notification-bell .badge', { hasText: '1' }).waitFor({ timeout: 30000 });
await other.locator('app-notification-bell .badge', { hasText: '1' }).waitFor({ timeout: 30000 });
await other.screenshot({ path: `${outDir}/01-unread-badge.png` });
step('another tab receives the completion notification in real time');

// 2. 打開鈴鐺：清單有這則未讀通知，點選前往對話並標為已讀
await other.click('app-notification-bell button.bell');
const item = other.locator('app-notification-bell .item').first();
await item.waitFor();
assert.match(await item.locator('.title').innerText(), /^已完成：/);
assert.ok(await item.evaluate((el) => el.classList.contains('unread')));
await other.screenshot({ path: `${outDir}/02-popover.png` });
await item.click();
await other.waitForURL((url) => url.pathname === conversationPath);
await other.locator('app-notification-bell .badge').waitFor({ state: 'detached', timeout: 10000 });
step('clicking the notification opens the conversation and marks it read');

// 3. 正在看同一個對話時完成：直接標為已讀，不出現未讀數
await other.fill('app-composer textarea', '再補充一點');
await other.press('app-composer textarea', 'Enter');
await other.click('app-notification-bell button.bell');
await other.locator('app-notification-bell .item').nth(1).waitFor({ timeout: 30000 });
await other
  .locator('app-notification-bell .item.unread')
  .waitFor({ state: 'detached', timeout: 10000 });
assert.equal(await other.locator('app-notification-bell .badge').count(), 0);
await other.screenshot({ path: `${outDir}/03-viewing-marks-read.png` });
step('a notification for the conversation being viewed is marked read immediately');

assert.deepEqual(errors, []);
await browser.close();
console.log('notifications flow passed');
