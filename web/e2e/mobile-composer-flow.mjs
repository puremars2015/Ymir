// Mobile composer UI verification with read-only API fixtures. No production records are modified.
// Usage: CHROMIUM_PATH=... node e2e/mobile-composer-flow.mjs [screenshotDirectory] [baseUrl]
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
const conversation = (id, title, projectId = null) => ({
  id,
  title,
  projectId,
  modelId: 'test',
  status: 'ACTIVE',
  createdAt: now,
  updatedAt: now,
  activeExecutionId: null,
});
const conversations = [
  conversation(id, '整理員工旅遊行程與景點'),
  conversation('33333333-3333-4333-8333-333333333333', '首頁設計與內容討論', projectId),
  conversation(
    '44444444-4444-4444-8444-444444444444',
    '這是一個很長的對話名稱，用來確認文字不會擠壓圖示與選單按鈕',
  ),
  conversation('55555555-5555-4555-8555-555555555555', 'PDF 文件摘要'),
];
await context.route('**/api/**', async (route) => {
  if (route.request().method() !== 'GET') throw new Error('Unexpected API mutation');
  const path = new URL(route.request().url()).pathname.replace(/\/$/, '');
  const data =
    path === '/api/me'
      ? {
          id,
          displayName: 'SeanMa',
          role: 'User',
          status: 'Active',
          authMethod: 'Password',
          mustChangePassword: false,
        }
      : path === '/api/models'
        ? [{ id: 'test', displayName: '測試模型', isDefault: true, supportsImages: false }]
        : path === '/api/projects'
          ? [{ id: projectId, name: '公司形象網站', createdAt: now, updatedAt: now }]
          : path === '/api/conversations'
            ? conversations
            : path.startsWith('/api/conversations/') && !path.match(/\/(messages|files|artifacts)$/)
              ? conversations.find((c) => path.endsWith(c.id))
              : path.endsWith('/messages')
                ? [
                    {
                      id: '66666666-6666-4666-8666-666666666666',
                      role: 'ASSISTANT',
                      messageType: 'TEXT',
                      content: '這裡保留目前的對話內容。',
                      sequenceNo: 1,
                      executionId: null,
                      createdAt: now,
                      attachments: [],
                    },
                  ]
                : path.endsWith('/files')
                  ? { files: [], truncated: false }
                  : path.endsWith('/artifacts') || path === '/api/make-topics'
                    ? []
                    : null;
  if (data == null) throw new Error(`No fixture for ${path}`);
  await route.fulfill({ json: data });
});
const page = await context.newPage();
const errors = [];
page.on('pageerror', (error) => errors.push(error.message));
const input = page.getByRole('textbox', { name: '訊息', exact: true });
const composer = page.locator('app-composer');
const checkBottom = async (bottom, selector = 'app-composer') => {
  await page.waitForFunction(
    ({ bottom, selector }) => {
      const box = document.querySelector(selector)?.getBoundingClientRect();
      return box && Math.abs(bottom - box.bottom - 8) < 2;
    },
    { bottom, selector },
  );
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
};
try {
  for (const size of [
    { width: 390, height: 844 },
    { width: 375, height: 667 },
    { width: 667, height: 375 },
  ]) {
    await page.setViewportSize(size);
    await page.goto(`${baseUrl}/c/${id}`);
    await input.waitFor();
    await checkBottom(size.height);
    assert.equal(await page.locator('.usage-hint').isVisible(), false);
    await input.fill('測試訊息');
    assert.ok(await page.getByRole('button', { name: '送出', exact: true }).isEnabled());
    await page.screenshot({ path: `${outDir}/chat-${size.width}x${size.height}.png` });
    await page.goto(baseUrl);
    await input.waitFor();
    await checkBottom(size.height);
    await page.screenshot({ path: `${outDir}/new-chat-${size.width}x${size.height}.png` });
  }
  console.log(
    'PASS mobile new/existing chat bottom alignment, portrait/landscape, send button and overflow',
  );
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(`${baseUrl}/c/${id}`);
  await input.waitFor();
  // Simulate keyboard changing only the visual viewport, without shrinking layout viewport.
  await page.evaluate(() => {
    const viewport = window.visualViewport;
    window.testViewport = { height: 844, top: 0, scale: 1 };
    Object.defineProperty(viewport, 'height', {
      get: () => window.testViewport.height,
      configurable: true,
    });
    Object.defineProperty(viewport, 'offsetTop', {
      get: () => window.testViewport.top,
      configurable: true,
    });
    Object.defineProperty(viewport, 'scale', {
      get: () => window.testViewport.scale,
      configurable: true,
    });
  });
  await input.fill(Array(30).fill('多行文字仍可捲動與送出').join('\n'));
  await input.focus();
  await page.evaluate(() => {
    window.testViewport.height = 410;
    window.visualViewport.dispatchEvent(new Event('resize'));
  });
  await checkBottom(410);
  await page
    .locator('app-composer input[type=file]')
    .setInputFiles({
      name: '測試附件.pdf',
      mimeType: 'application/pdf',
      buffer: Buffer.from('%PDF-1.4 test'),
    });
  await page.locator('.attachment').waitFor();
  await checkBottom(410);
  const sendBox = await page.getByRole('button', { name: '送出', exact: true }).boundingBox();
  assert.ok(sendBox.y + sendBox.height <= 410);
  await page.getByRole('button', { name: '移除 測試附件.pdf', exact: true }).click();
  const scroller = page.locator('.scroller');
  assert.ok((await scroller.boundingBox()).height >= 0);
  await page.screenshot({ path: `${outDir}/keyboard-multiline.png` });
  await page.evaluate(() => {
    window.testViewport.top = 75;
    window.visualViewport.dispatchEvent(new Event('scroll'));
  });
  await checkBottom(485);
  // Pinch zoom must not resize the app layout.
  const originalHeight = (await page.locator('app-shell').boundingBox()).height;
  await page.evaluate(() => {
    window.testViewport.scale = 2;
    window.testViewport.height = 205;
    window.visualViewport.dispatchEvent(new Event('resize'));
  });
  assert.equal((await page.locator('app-shell').boundingBox()).height, originalHeight);
  await page.evaluate(() => {
    window.testViewport = { height: 844, top: 0, scale: 1 };
    window.visualViewport.dispatchEvent(new Event('resize'));
  });
  await checkBottom(844);
  await input.fill('');
  // Long conversation stays in its own scroll area without moving the composer.
  await page.route('**/api/conversations/*/messages', (route) =>
    route.fulfill({
      json: Array.from({ length: 40 }, (_, i) => ({
        id: `message-${i}`,
        role: i % 2 ? 'ASSISTANT' : 'USER',
        messageType: 'TEXT',
        content: '較長的對話內容，用來確認只有訊息區捲動。',
        sequenceNo: i + 1,
        executionId: null,
        createdAt: now,
        attachments: [],
      })),
    }),
  );
  await page.reload();
  await page.locator('.turn').nth(39).waitFor();
  await checkBottom(844);
  assert.ok(await scroller.evaluate((el) => el.scrollHeight > el.clientHeight));
  await scroller.evaluate((el) => (el.scrollTop = -el.scrollHeight));
  await checkBottom(844);
  await page.screenshot({ path: `${outDir}/long-chat.png` });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.waitForFunction(
    () => !document.querySelector('app-shell').style.getPropertyValue('--viewport-height'),
  );
  assert.ok(await page.locator('.usage-hint').isVisible());
  assert.equal((await page.locator('app-shell').boundingBox()).height, 1000);
  assert.deepEqual(errors, []);
  console.log(
    'PASS keyboard resize/pan/close, multiline, zoom preservation, long conversation scroll and desktop restore',
  );
} catch (error) {
  console.log(
    await page.evaluate(() =>
      Object.fromEntries(
        [
          'app-shell',
          'app-new-chat-page',
          '.welcome',
          '.welcome-copy',
          '.welcome-composer',
          'app-composer',
        ].map((s) => [s, document.querySelector(s)?.getBoundingClientRect().toJSON()]),
      ),
    ),
  );
  await page.screenshot({ path: `${outDir}/failure.png` });
  throw error;
} finally {
  await browser.close();
}
