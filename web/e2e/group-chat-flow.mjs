// 群組聊天與 @bot（ADR-0026）端對端驗證：建立群組、即時收發與未讀、@bot 即時回答與完成、成員管理、窄螢幕。
// 前置：SQL Server、API（Development，Scripted harness 即可）、ng serve（或 API 提供 Angular build）都已啟動。
// 用法：node e2e/group-chat-flow.mjs <screenshot-dir> [baseUrl]
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';
import assert from 'node:assert/strict';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const executablePath = process.env.CHROMIUM_PATH;
const browser = await chromium.launch(executablePath ? { executablePath } : {});
const step = (name) => console.log(`✔ ${name}`);
const suffix = Date.now().toString(36);

async function login(account, viewport = { width: 1440, height: 900 }) {
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
  await page.click('details.dev button[type=submit]');
  await page.waitForSelector('app-new-chat-page h1');
  return { context, page, errors };
}

const bob = await login(`grp-bob-${suffix}`);
const alice = await login(`grp-alice-${suffix}`);
const bobName = await bob.page.locator('.user .name').innerText();
const groupName = `季報小組 ${suffix}`;

// 1. Alice 從「群組」分頁建立群組並加入 Bob
const panel = alice.page.locator('app-people-panel');
await panel.locator('.tabs button:has-text("群組")').click();
await panel.locator('button:has-text("建立群組")').click();
await panel.locator('input[name=groupName]').fill(groupName);
await panel.locator('input[aria-label="搜尋成員"]').fill(bobName);
await panel.locator(`.pick:has-text("${bobName}") input[type=checkbox]`).check();
await panel.locator('button[type=submit]:has-text("建立")').click();
await panel.locator(`.peer strong:has-text("${groupName}")`).waitFor();
step('alice created a group with bob');

// 2. Bob 的群組分頁即時出現新群組與未讀數
const bobPanel = bob.page.locator('app-people-panel');
await bobPanel.locator('.tabs button:has-text("群組")').click();
await bobPanel.locator(`.contact:has-text("${groupName}")`).waitFor({ timeout: 15000 });
await alice.page.fill('textarea[aria-label="群組訊息"]', '大家好，明天交季報');
await alice.page.press('textarea[aria-label="群組訊息"]', 'Enter');
await bobPanel.locator(`.contact:has-text("${groupName}") .badge`).waitFor({ timeout: 15000 });
step('bob sees the new group and an unread badge in real time');

// 3. Bob 開啟群組並回覆；Alice 即時看到（含寄件者名稱）
await bobPanel.locator(`.contact:has-text("${groupName}")`).click();
await bobPanel.locator('.bubble:has-text("大家好，明天交季報")').waitFor();
await bob.page.fill('textarea[aria-label="群組訊息"]', '收到 <b>不會被當成 HTML</b>');
await bob.page.press('textarea[aria-label="群組訊息"]', 'Enter');
await panel.locator('.bubble:has-text("收到 <b>不會被當成 HTML</b>")').waitFor({ timeout: 15000 });
assert.equal(await panel.locator('.bubble b').count(), 0, 'message must be text, not HTML');
await panel.locator(`.sender:has-text("${bobName}")`).first().waitFor();
step('members chat in real time; messages are text only');

// 4. Alice @bot：輸入框提示費用、Bot 訊息即時顯示「思考中」→ 完成；Bob 也看得到
await alice.page.fill('textarea[aria-label="群組訊息"]', '@bot 幫我整理重點');
await panel.locator('.hint:has-text("費用記在你的帳號")').waitFor();
await alice.page.press('textarea[aria-label="群組訊息"]', 'Enter');
await panel.locator('.message.bot').first().waitFor({ timeout: 15000 });
await panel.locator('.message.bot .bubble:not(.pending)').waitFor({ timeout: 60000 });
const reply = await panel.locator('.message.bot .bubble').last().innerText();
assert.ok(reply.includes('幫我整理重點'), `bot reply should answer the request: ${reply}`);
await bobPanel.locator('.message.bot .bubble:not(.pending)').waitFor({ timeout: 30000 });
assert.ok(
  (await bobPanel.locator('.message.bot .sender').last().innerText()).includes('回覆'),
  'bot reply shows who called it',
);
step('@bot replied in the group (caller and other members see it)');
await alice.page.screenshot({ path: `${outDir}/group-bot.png` });

// 5. 管理：Bob 不是擁有者看不到移除按鈕；Alice 改名；Bob 退出後群組從清單消失
await bobPanel.locator('button[aria-label="群組成員與設定"]').click();
assert.equal(await bobPanel.locator('.member button:has-text("移除")').count(), 0);
await panel.locator('button[aria-label="群組成員與設定"]').click();
await panel.locator('input[aria-label="群組名稱"]').fill(`${groupName} v2`);
await panel.locator('button:has-text("改名")').click();
await panel.locator(`.peer strong:has-text("${groupName} v2")`).waitFor();
await bobPanel.locator('button:has-text("退出群組")').click();
await bobPanel.locator('.contacts').waitFor();
await bob.page.waitForTimeout(500);
assert.equal(await bobPanel.locator(`.contact:has-text("${groupName}")`).count(), 0);
await panel.locator('.member').first().waitFor();
await alice.page.waitForFunction(() => document.querySelectorAll('app-people-panel .member').length === 1, null, { timeout: 15000 });
step('only the owner manages; member left and lost the group');
await alice.page.screenshot({ path: `${outDir}/group-manage.png` });

// 6. 窄螢幕：成員抽屜中的群組分頁與對話
const mobile = await login(`grp-mobile-${suffix}`, { width: 390, height: 844 });
await mobile.page.click('button.people-toggle');
const mobilePanel = mobile.page.locator('app-people-panel');
await mobilePanel.locator('.tabs button:has-text("群組")').click();
await mobilePanel.locator('button:has-text("建立群組")').waitFor();
const overflow = await mobile.page.evaluate(
  () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
);
assert.ok(overflow <= 1, `no horizontal scroll on mobile (${overflow}px)`);
await mobile.page.screenshot({ path: `${outDir}/group-mobile.png` });
step('mobile drawer shows the groups tab');

for (const session of [alice, bob, mobile]) {
  assert.deepEqual(session.errors, [], 'no page errors');
}
await browser.close();
