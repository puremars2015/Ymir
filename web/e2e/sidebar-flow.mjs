// Sidebar UI verification with read-only API fixtures. No production records are modified.
// Usage: CHROMIUM_PATH=... node e2e/sidebar-flow.mjs [screenshotDirectory] [baseUrl]
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';
import assert from 'node:assert/strict';
const [outDir = '.', baseUrl = 'http://localhost:4301'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH });
const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
const id = '11111111-1111-4111-8111-111111111111';
const projectId = '22222222-2222-4222-8222-222222222222';
const now = '2026-10-07T08:00:00Z';
const conversation = (id, title, projectId = null) => ({ id, title, projectId, modelId: 'test', status: 'ACTIVE', createdAt: now, updatedAt: now, activeExecutionId: null });
const conversations = [conversation(id, '整理員工旅遊行程與景點'), conversation('33333333-3333-4333-8333-333333333333', '首頁設計與內容討論', projectId), conversation('44444444-4444-4444-8444-444444444444', '這是一個很長的對話名稱，用來確認文字不會擠壓圖示與選單按鈕'), conversation('55555555-5555-4555-8555-555555555555', 'PDF 文件摘要')];
await context.route('**/api/**', async route => {
  if (route.request().method() !== 'GET') throw new Error('Unexpected API mutation');
  const path = new URL(route.request().url()).pathname.replace(/\/$/, '');
  const data = path === '/api/me' ? { id, displayName: 'SeanMa', role: 'User', status: 'Active', authMethod: 'Password', mustChangePassword: false }
    : path === '/api/models' ? [{ id: 'test', displayName: '測試模型', isDefault: true, supportsImages: false }]
    : path === '/api/projects' ? [{ id: projectId, name: '公司形象網站', createdAt: now, updatedAt: now }]
    : path === '/api/conversations' ? conversations
    : path.startsWith('/api/conversations/') && !path.match(/\/(messages|files|artifacts)$/) ? conversations.find(c => path.endsWith(c.id))
    : path.endsWith('/messages') ? [{ id: '66666666-6666-4666-8666-666666666666', role: 'ASSISTANT', messageType: 'TEXT', content: '這裡保留目前的對話內容。', sequenceNo: 1, executionId: null, createdAt: now, attachments: [] }]
    : path.endsWith('/files') ? { files: [], truncated: false }
    : path.endsWith('/artifacts') || path === '/api/make-topics' ? [] : null;
  if (data == null) throw new Error(`No fixture for ${path}`);
  await route.fulfill({ json: data });
});
const page = await context.newPage();
const errors = [];
page.on('pageerror', error => errors.push(error.message));
const toggle = page.locator('.sidebar-toggle');
const sidebar = page.locator('.sidebar');
const visibleState = async open => {
  await page.waitForFunction(expected => document.querySelector('.sidebar-toggle')?.getAttribute('aria-expanded') === String(expected), open);
  assert.equal(await sidebar.evaluate(el => el.inert), !open);
};
try {
  await page.goto(`${baseUrl}/c/${id}`);
  await page.locator('.sidebar a.item.active').waitFor();
  await visibleState(true);
  await page.getByRole('button', { name: '展開 公司形象網站', exact: true }).click();
  await page.locator('.nested a').waitFor();
  const firstSection = await page.locator('.sections .section').first().boundingBox();
  const secondSection = await page.locator('.sections .section').nth(1).boundingBox();
  assert.ok(secondSection.y - (firstSection.y + firstSection.height) >= 24);
  assert.ok((await page.locator('.conversation-link').last().boundingBox()).height >= 44);
  await page.screenshot({ path: `${outDir}/desktop-expanded.png` });
  await toggle.click();
  await visibleState(false);
  assert.equal(await sidebar.isVisible(), false);
  assert.equal((await page.locator('.content').boundingBox()).x, 0);
  await page.reload();
  await visibleState(false);
  await toggle.focus();
  await page.keyboard.press('Enter');
  await visibleState(true);
  await page.goto(`${baseUrl}/c/33333333-3333-4333-8333-333333333333`);
  await page.locator('.nested a.active').waitFor();
  await page.getByRole('button', { name: '收合 公司形象網站', exact: true }).click();
  await page.locator('.nested').waitFor({ state: 'detached' });
  console.log('PASS desktop layout, group spacing, persistent collapse, keyboard toggle and active-project folding');
  await page.setViewportSize({ width: 390, height: 844 });
  await visibleState(false);
  await toggle.click();
  await visibleState(true);
  await page.waitForFunction(() => document.activeElement?.classList.contains('new-chat'));
  await page.waitForFunction(() => Math.abs(document.querySelector('.sidebar').getBoundingClientRect().x) < 1);
  await page.screenshot({ path: `${outDir}/mobile-drawer.png` });
  await page.locator('.sidebar button:has-text("登出")').focus();
  await page.keyboard.press('Tab');
  assert.ok(await page.evaluate(() => document.querySelector('.sidebar').contains(document.activeElement)));
  await page.keyboard.press('Escape');
  await visibleState(false);
  assert.ok(await toggle.evaluate(el => document.activeElement === el));
  await toggle.click();
  await page.locator('.backdrop').click({ position: { x: 380, y: 400 } });
  await visibleState(false);
  await toggle.click();
  await page.locator(`.sidebar a[href="/c/${id}"]`).click();
  await visibleState(false);
  await page.setViewportSize({ width: 1440, height: 1000 });
  await visibleState(true);
  await page.getByRole('button', { name: '深色', exact: true }).click();
  await page.screenshot({ path: `${outDir}/desktop-dark.png` });
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
  assert.deepEqual(errors, []);
  console.log('PASS mobile drawer, focus containment, Escape, backdrop, navigation close, responsive resize and dark theme');
} finally { await browser.close(); }
