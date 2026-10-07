// Model picker UI verification with read-only API fixtures. No production records are modified.
// Usage: CHROMIUM_PATH=... node e2e/model-picker-flow.mjs [screenshotDirectory] [baseUrl]
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
const submissions = [];
await context.route('**/api/**', async (route) => {
  if (route.request().method() === 'POST' && route.request().url().endsWith('/messages')) {
    submissions.push(route.request().postDataJSON());
    return route.fulfill({ status: 400, json: { detail: '測試訊息已捕捉' } });
  }
  if (
    route.request().method() === 'POST' &&
    new URL(route.request().url()).pathname === '/api/conversations'
  ) {
    return route.fulfill({ status: 201, json: conversation(id, '測試新對話') });
  }
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
        ? [
            { id: 'test', displayName: 'MiniMax-M2.7', isDefault: true, supportsImages: false },
            {
              id: 'openrouter-sonnet-5.5',
              displayName: 'Claude Sonnet 5.5 (OpenRouter)',
              isDefault: false,
              supportsImages: true,
              supportsThinking: true,
            },
            {
              id: 'openrouter-gpt-6.1-sol',
              displayName: 'GPT-6.1 Sol (OpenRouter)',
              isDefault: false,
              supportsImages: true,
              supportsThinking: true,
            },
            {
              id: 'openrouter-gpt-6-luna',
              displayName: 'GPT-6 Luna (OpenRouter)',
              isDefault: false,
              supportsImages: true,
              supportsThinking: true,
            },
          ]
        : path === '/api/projects'
          ? [{ id: projectId, name: '公司形象網站', createdAt: now, updatedAt: now }]
          : path === `/api/projects/${projectId}`
            ? {
                id: projectId,
                name: '公司形象網站',
                systemPrompt: '',
                createdAt: now,
                updatedAt: now,
              }
            : path === '/api/conversations'
              ? conversations
              : path.startsWith('/api/conversations/') &&
                  !path.match(/\/(messages|files|artifacts)$/)
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
const picker = page.getByRole('button', { name: '模型與思考設定', exact: true });
const panel = page.getByRole('dialog', { name: '模型與思考設定' });
const list = page.getByRole('listbox', { name: '模型', exact: true });
const summary = page.locator('app-model-picker .selection-summary');
async function selected(name) {
  await page.waitForFunction(
    (name) =>
      document
        .querySelector('app-model-picker .selection-summary .model-name')
        ?.textContent.trim() === name,
    name,
  );
}
try {
  await page.goto(baseUrl);
  await picker.waitFor();
  await picker.click();
  await panel.waitFor();
  assert.equal(await page.getByRole('option').count(), 4);
  assert.equal((await list.innerText()).includes('OpenRouter'), false);
  assert.ok(await page.getByRole('radio', { name: '深入', exact: true }).isDisabled());
  const sonnet = page.getByRole('option', { name: 'Claude Sonnet 5.5', exact: true });
  assert.equal(await sonnet.getByRole('img').count(), 2);
  await sonnet.click();
  await selected('Claude Sonnet 5.5');
  await page.getByRole('radio', { name: '深入', exact: true }).click();
  await page.waitForFunction(() =>
    document.querySelector('app-model-picker .depth-summary')?.textContent.includes('深入'),
  );
  for (const modelName of ['GPT-6 Luna', 'GPT-6.1 Sol', 'Claude Sonnet 5.5']) {
    await page.getByRole('option', { name: modelName, exact: true }).click();
    await selected(modelName);
    for (const depthName of ['輕量', '標準', '深入', '自動']) {
      await page
        .getByRole('radio', { name: depthName, exact: true })
        .click({ position: { x: 8, y: 8 } });
      await page.waitForFunction(
        (label) =>
          document.querySelector('app-model-picker .depth-summary')?.textContent.includes(label),
        depthName,
        { timeout: 3000 },
      );
    }
  }
  await page.getByRole('radio', { name: '深入', exact: true }).click();
  await page.screenshot({ path: `${outDir}/desktop-model-settings.png` });
  await page.keyboard.press('Escape');
  await panel.waitFor({ state: 'detached' });
  await page.waitForFunction(() =>
    document.activeElement?.matches('app-model-picker button.picker'),
  );
  assert.equal(await page.evaluate(() => localStorage.getItem('ymir.preferredThinking')), 'high');
  await page.reload();
  await selected('Claude Sonnet 5.5');
  assert.ok((await summary.innerText()).includes('深入'));
  await picker.focus();
  await page.keyboard.press('ArrowDown');
  await list.waitFor();
  await page.keyboard.press('End');
  await page.keyboard.press('Enter');
  await selected('GPT-6 Luna');
  assert.equal(await panel.count(), 1);
  await page.keyboard.press('Tab');
  await page.waitForFunction(() => document.activeElement?.getAttribute('role') === 'radio');
  await page.keyboard.press('ArrowLeft');
  await page.waitForFunction(() =>
    document.querySelector('app-model-picker .depth-summary')?.textContent.includes('標準'),
  );
  assert.equal(
    await page.getByRole('radio', { name: '標準', exact: true }).getAttribute('aria-checked'),
    'true',
  );
  await page.keyboard.press('Escape');
  await picker.click();
  await page.getByRole('textbox', { name: '訊息', exact: true }).click();
  await panel.waitFor({ state: 'detached' });
  console.log(
    'PASS icon panel, model-only labels, modality icons, thinking selection, persistence, keyboard and outside closing',
  );

  await page.goto(`${baseUrl}/c/${id}`);
  await picker.click();
  await sonnet.click();
  await selected('Claude Sonnet 5.5');
  await page.getByRole('radio', { name: '標準', exact: true }).click();
  await page.keyboard.press('Escape');
  const chatResponse = page.waitForResponse(
    (r) => r.url().endsWith('/messages') && r.request().method() === 'POST',
  );
  await page.getByRole('textbox', { name: '訊息', exact: true }).fill('測試既有對話');
  await page.getByRole('textbox', { name: '訊息', exact: true }).press('Enter');
  await chatResponse;
  assert.equal(submissions.at(-1).thinkingLevel, 'medium');
  assert.equal(submissions.at(-1).modelId, 'openrouter-sonnet-5.5');

  for (const [modelName, modelId] of [
    ['GPT-6 Luna', 'openrouter-gpt-6-luna'],
    ['GPT-6.1 Sol', 'openrouter-gpt-6.1-sol'],
  ]) {
    await picker.click();
    await page.getByRole('option', { name: modelName, exact: true }).click();
    await page
      .getByRole('radio', { name: '深入', exact: true })
      .click({ position: { x: 8, y: 8 } });
    await page.keyboard.press('Escape');
    const response = page.waitForResponse(
      (r) => r.url().endsWith('/messages') && r.request().method() === 'POST',
    );
    await page.getByRole('textbox', { name: '訊息', exact: true }).fill('驗證模型思考深度');
    await page.getByRole('textbox', { name: '訊息', exact: true }).press('Enter');
    await response;
    assert.equal(submissions.at(-1).thinkingLevel, 'high');
    assert.equal(submissions.at(-1).modelId, modelId);
  }

  for (const target of ['', `/projects/${projectId}`]) {
    await page.goto(baseUrl + target);
    await picker.click();
    await sonnet.click();
    await page.getByRole('radio', { name: '輕量', exact: true }).click();
    await page.keyboard.press('Escape');
    const response = page.waitForResponse(
      (r) => r.url().endsWith('/messages') && r.request().method() === 'POST',
    );
    await page.getByRole('textbox', { name: '訊息', exact: true }).fill('測試第一則訊息');
    await page.getByRole('textbox', { name: '訊息', exact: true }).press('Enter');
    await response;
    assert.equal(submissions.at(-1).thinkingLevel, 'low');
  }
  await page.goto(`${baseUrl}/c/${id}`);
  await picker.click();
  await page.getByRole('option', { name: 'MiniMax-M2.7', exact: true }).click();
  await selected('MiniMax-M2.7');
  await page.keyboard.press('Escape');
  const unsupportedResponse = page.waitForResponse(
    (r) => r.url().endsWith('/messages') && r.request().method() === 'POST',
  );
  await page.getByRole('textbox', { name: '訊息', exact: true }).fill('固定深度');
  await page.getByRole('textbox', { name: '訊息', exact: true }).press('Enter');
  await unsupportedResponse;
  assert.equal(submissions.at(-1).thinkingLevel, null);
  console.log(
    'PASS existing chat and first-message model/depth snapshots; unsupported model never receives effort',
  );

  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(`${baseUrl}/c/${id}`);
  await picker.click();
  await sonnet.click();
  await selected('Claude Sonnet 5.5');
  await page.getByRole('radio', { name: '深入', exact: true }).click();
  let box = await panel.boundingBox();
  let anchor = await picker.boundingBox();
  assert.ok(box.x >= 0 && box.x + box.width <= 390);
  assert.ok(box.y >= 0 && box.y + box.height <= anchor.y);
  await page.screenshot({ path: `${outDir}/mobile-model-settings.png` });
  await page.keyboard.press('Escape');
  await page.locator('.sidebar-toggle').click();
  await page.getByRole('button', { name: '深色', exact: true }).click();
  await page.keyboard.press('Escape');
  await page.waitForFunction(
    () => document.querySelector('.sidebar').getBoundingClientRect().right <= 1,
  );
  await picker.click();
  await page.screenshot({ path: `${outDir}/mobile-model-settings-dark.png` });
  await page.setViewportSize({ width: 375, height: 410 });
  await panel.waitFor({ state: 'detached' });
  await picker.click();
  await panel.waitFor();
  box = await panel.boundingBox();
  assert.ok(box.y >= 0 && box.y + box.height <= 410);
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
  assert.deepEqual(errors, []);
  console.log('PASS mobile above-anchor placement, dark theme and keyboard-sized viewport');
} finally {
  await browser.close();
}
