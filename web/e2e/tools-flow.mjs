// 左側「小工具」的端對端驗證（ADR-0029）：/make → 工作目錄出現 tool/index.html → 執行完成自動發布 →
// 側邊欄出現小工具 → 在 Ymir 內以 iframe 開啟（網站網域、私人網站票據）→ 其他人看不到。
// 前置：同 sites-flow.mjs（SQL Server、API 的 Ymir__Sites__* 與 VibeMaker__Runtime__WorkspaceRoot、SiteHost、ng serve）；
//       SiteHost 的 --SiteHost:PlatformUrl 要是 Angular 的網址（frame-ancestors 只允許它嵌入）。
// 用法：WORKSPACE_ROOT=<工作目錄根> node e2e/tools-flow.mjs <screenshot-dir> [baseUrl]
import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
const workspaceRoot = process.env.WORKSPACE_ROOT;
if (!workspaceRoot) throw new Error('WORKSPACE_ROOT is required');
mkdirSync(outDir, { recursive: true });
const browser = await chromium.launch(
  process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {},
);
const step = (name) => console.log(`✔ ${name}`);
const stamp = Date.now();

const devLogin = async (account) => {
  const context = await browser.newContext({ viewport: { width: 1280, height: 860 } });
  const page = await context.newPage();
  page.on('dialog', (dialog) => void dialog.accept());
  await page.goto(`${baseUrl}/login`);
  await page.waitForSelector('details.dev');
  if (!(await page.locator('details.dev').evaluate((d) => d.open)))
    await page.click('details.dev summary');
  await page.fill('input[name=devAccount]', account);
  await page.click('details.dev button[type=submit]');
  await page.waitForSelector('app-new-chat-page h1');
  return page;
};
const me = (page) => page.evaluate(() => fetch('/api/me').then((r) => r.json()));
// 送出並等到第 replies 則 Agent 回覆存進資料庫（執行完成；小工具在完成事件之前就已發布）。
const send = async (page, text, replies) => {
  await page.fill('app-composer textarea', text);
  await page.press('app-composer textarea', 'Enter');
  await page.waitForURL(/\/c\//);
  const id = page.url().split('/c/')[1].split(/[?#]/)[0];
  await page.waitForFunction(
    async ([conversationId, count]) => {
      const response = await fetch(`/api/conversations/${conversationId}/messages`);
      if (!response.ok) return false;
      const body = await response.json();
      const messages = Array.isArray(body) ? body : (body.messages ?? body.items ?? []);
      return messages.filter((m) => m.role === 'ASSISTANT').length >= count;
    },
    [id, replies],
    { timeout: 60000, polling: 500 },
  );
  await page.waitForSelector('.turn.live', { state: 'detached', timeout: 60000 });
};

const owner = await devLogin(`tool-owner-${stamp}`);
const other = await devLogin(`tool-other-${stamp}`);

// 1. 小工具區塊一開始是空的提示
const toolsSection = (page) =>
  page.locator('nav.sections section').filter({ has: page.locator('header', { hasText: '小工具' }) });
await toolsSection(owner).waitFor();
assert.match(await toolsSection(owner).innerText(), /\/make 小工具架設/);
step('sidebar shows the empty tools section');

// 2. /make 描述（Agent 可能選小工具主題）→ 還沒有成果，不發布
await send(owner, '/make 做一個 BMI 計算機', 1);
assert.equal(await toolsSection(owner).locator('a.item').count(), 0);
const conversationId = owner.url().split('/c/')[1].split(/[?#]/)[0];
step('no tool before tool/index.html exists');

// 3. Agent 建好 tool/index.html（直接寫進工作目錄），下一次執行完成就自動發布
const userId = (await me(owner)).id.replaceAll('-', '');
const toolDir = join(
  workspaceRoot,
  'users',
  userId,
  'workspace',
  'chats',
  conversationId.replaceAll('-', ''),
  'tool',
);
mkdirSync(toolDir, { recursive: true });
writeFileSync(
  join(toolDir, 'index.html'),
  '<!doctype html><meta charset="utf-8"><title>BMI</title><h1>BMI 計算機</h1>' +
    '<button id="b" onclick="document.getElementById(\'r\').textContent = String(6 * 7)">算</button><p id="r"></p>',
);
await send(owner, '好，就這樣', 2);
const link = toolsSection(owner).locator('a.item').first();
await link.waitFor({ timeout: 15000 });
step('tool appears in the sidebar after the run completes');

// 4. 在 Ymir 內以 iframe 開啟：內容來自網站網域、腳本可以執行
await link.click();
await owner.waitForURL(/\/tools\//);
const frame = owner.frameLocator('app-tool-page iframe');
await frame.locator('h1').waitFor({ timeout: 15000 });
assert.equal(await frame.locator('h1').innerText(), 'BMI 計算機');
await frame.locator('#b').click();
assert.equal(await frame.locator('#r').innerText(), '42');
const src = await owner.locator('app-tool-page iframe').getAttribute('src');
assert.match(src, /^http:\/\/[a-z0-9]{8}\.sites\.localhost:5300\//);
await owner.screenshot({ path: join(outDir, 'tool-open.png') });
step('tool opens inside Ymir from the site domain and runs its script');

// 5. 其他人看不到，也打不開
assert.equal(await toolsSection(other).locator('a.item').count(), 0);
await other.goto(`${baseUrl}${new URL(owner.url()).pathname}`);
await other.locator('app-tool-page .error').waitFor();
step('other users cannot see or open the tool');

// 6. 手機寬度
await owner.setViewportSize({ width: 390, height: 780 });
await owner.reload();
await owner.frameLocator('app-tool-page iframe').locator('h1').waitFor({ timeout: 15000 });
await owner.screenshot({ path: join(outDir, 'tool-mobile.png') });
step('tool page works at phone width');

await browser.close();
