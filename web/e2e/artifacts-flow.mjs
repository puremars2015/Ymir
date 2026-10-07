// UI end-to-end checks with deterministic API fixtures; no production data is written.
// Backend ownership, ZIP contents and path protection are covered by ArtifactTests.
// Usage: node e2e/artifacts-flow.mjs [outputDirectory] [baseUrl]
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';
import assert from 'node:assert/strict';
const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH });
const context = await browser.newContext({ viewport: { width: 1360, height: 900 }, acceptDownloads: true });
const id = '11111111-1111-4111-8111-111111111111';
const executionId = '22222222-2222-4222-8222-222222222222';
const messageId = '33333333-3333-4333-8333-333333333333';
const now = '2026-10-07T08:00:00Z';
const conversation = { id, title: '交付成果驗證', projectId: null, modelId: 'test', status: 'ACTIVE', createdAt: now, updatedAt: now, activeExecutionId: null };
const file = path => ({ path, size: 18, modifiedAt: now });
let artifacts = [];
let reply = '分析已完成。';
await context.route('**/api/**', async route => {
  const url = new URL(route.request().url());
  const path = url.pathname.replace(/\/$/, '');
  if (route.request().method() !== 'GET') throw new Error(`Unexpected production mutation: ${path}`);
  if (path.endsWith('/download')) return route.fulfill({ contentType: 'application/octet-stream', headers: { 'Content-Disposition': 'attachment; filename="summary.md"' }, body: 'Hello from Ymir' });
  if (path.endsWith('/archive')) return route.fulfill({ contentType: 'application/zip', headers: { 'Content-Disposition': 'attachment; filename="deliverables.zip"' }, body: Buffer.from('zip fixture') });
  const data = path === '/api/me' ? { id, displayName: '成果測試', accountName: 'fixture', role: 'User', status: 'Active', authMethod: 'Password', mustChangePassword: false }
    : path === '/api/models' ? [{ id: 'test', displayName: '測試模型', isDefault: true, supportsImages: false }]
    : path === '/api/projects' || path === '/api/make-topics' ? []
    : path === '/api/conversations' ? [conversation]
    : path === `/api/conversations/${id}` ? conversation
    : path.endsWith('/messages') ? [{ id: messageId, role: 'ASSISTANT', messageType: 'TEXT', content: reply, sequenceNo: 1, executionId, createdAt: now, attachments: [] }]
    : path.endsWith('/files') ? { files: [file('package.json'), file('package-lock.json'), file('attachments/manual.pdf')], truncated: false }
    : path.endsWith('/artifacts') ? artifacts
    : null;
  if (data === null) throw new Error(`Missing API fixture: ${path}`);
  return route.fulfill({ json: data });
});
const page = await context.newPage();
const errors = [];
page.on('pageerror', error => errors.push(error.message));
try {
  await page.goto(`${baseUrl}/c/${id}`);
  await page.locator('.turn.assistant').waitFor();
  await page.click('button.files-toggle');
  await page.getByText('尚無交付成果。', { exact: false }).waitFor();
  assert.equal(await page.locator('app-artifact-download a').count(), 0);
  await page.getByRole('button', { name: '專案檔案', exact: true }).click();
  await page.locator('app-files-panel li.file:has-text("package.json")').waitFor();
  assert.equal(await page.locator('app-files-panel li.file').count(), 3);
  await page.getByRole('link', { name: '下載專案檔案（ZIP）', exact: false }).waitFor();
  await page.screenshot({ path: `${outDir}/analysis-workspace.png` });
  console.log('PASS analysis has no download card; original files remain in workspace tab');
  artifacts = [{ executionId, conversationId: id, messageId, createdAt: now, files: [file('summary.md')] }];
  await page.reload();
  const single = page.locator('.turn.assistant app-artifact-download a');
  await single.waitFor();
  assert.match(await single.getAttribute('href'), /\/download\?path=summary.md$/);
  const [download] = await Promise.all([page.waitForEvent('download'), single.click()]);
  assert.match(download.url(), /\/download\?path=summary.md$/);
  await page.click('button.files-toggle');
  assert.equal(await page.getByRole('button', { name: '成果', exact: true }).getAttribute('aria-pressed'), 'true');
  await page.locator('app-files-panel button.open:has-text("summary.md")').click();
  await page.locator('app-files-panel .md:has-text("Hello from Ymir")').waitFor();
  await page.screenshot({ path: `${outDir}/single-preview.png` });
  console.log('PASS single file download, default results tab, and preview after reload');
  artifacts[0].files = [file('src/index.html'), file('package.json'), file('README.md')];
  await page.reload();
  const zip = page.locator('.turn.assistant app-artifact-download a');
  await zip.waitFor();
  assert.equal(await zip.count(), 1);
  assert.match(await zip.getAttribute('href'), /\/archive$/);
  assert.match(await zip.textContent(), /3 個檔案/);
  const [archive] = await Promise.all([page.waitForEvent('download'), zip.click()]);
  assert.match(archive.url(), /\/archive$/);
  await page.click('button.files-toggle');
  await page.locator('app-files-panel li.file:has-text("package.json")').waitFor();
  assert.equal(await page.locator('app-files-panel li.file').count(), 3);
  await page.screenshot({ path: `${outDir}/multiple-results.png` });
  console.log('PASS multiple deliverables recommend one ZIP; legitimate package.json visible in results');
  reply = '';
  await page.reload();
  await page.locator('.turn.assistant app-artifact-download a').waitFor();
  console.log('PASS delivery-only reply still has a download card');
  assert.deepEqual(errors, []);
} finally { await browser.close(); }
