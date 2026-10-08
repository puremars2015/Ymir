// Stateful browser fixtures exercise SSE display without executing commands or calling models.
import assert from 'node:assert/strict';
import { mkdirSync } from 'node:fs';
import { chromium } from 'playwright';
const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const browser = await chromium.launch(
  process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {},
);
try {
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  await context.addInitScript(() => {
    window.__ymirStreams = [];
    window.EventSource = class {
      listeners = new Map();
      closed = false;
      constructor() {
        window.__ymirStreams.push(this);
      }
      addEventListener(type, listener) {
        this.listeners.set(type, listener);
      }
      close() {
        this.closed = true;
      }
      emit(type, data) {
        if (!this.closed)
          this.listeners.get(type)?.(new MessageEvent(type, { data: JSON.stringify(data) }));
      }
    };
  });
  const id = '11111111-1111-4111-8111-111111111111';
  const executionId = '22222222-2222-4222-8222-222222222222';
  let finished = false;
  let failed = false;
  const conversation = () => ({
    id,
    title: '工具提示驗證',
    projectId: null,
    modelId: 'fake',
    activeExecutionId: finished ? null : executionId,
    createdAt: '2026-10-08T00:00:00Z',
  });
  const message = (role, content) => ({
    id: role,
    role,
    content,
    attachments: [],
    createdAt: '2026-10-08T00:00:00Z',
  });
  await context.route('**/api/**', async (route) => {
    const path = new URL(route.request().url()).pathname.replace(/\/$/, '');
    let data = [];
    if (path === '/api/me')
      data = {
        id,
        displayName: 'Tester',
        role: 'User',
        status: 'Active',
        authMethod: 'Local',
        mustChangePassword: false,
      };
    else if (path === '/api/models')
      data = [
        {
          id: 'fake',
          displayName: 'Fake Model',
          isDefault: true,
          supportsImages: false,
          supportsThinking: false,
        },
      ];
    else if (path === '/api/conversations') data = [conversation()];
    else if (path === `/api/conversations/${id}`) data = conversation();
    else if (path.endsWith('/messages'))
      data = [
        message('USER', '請處理文件'),
        ...(finished ? [message('ASSISTANT', '正常回覆內容')] : []),
        ...(finished && failed
          ? [{ ...message('ASSISTANT', '無法完成工作'), id: 'error', messageType: 'ERROR' }]
          : []),
      ];
    else if (path.endsWith('/files')) data = { files: [], truncated: false };
    await route.fulfill({ json: data });
  });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  const emit = (type, data) =>
    page.evaluate(({ type, data }) => window.__ymirStreams.at(-1).emit(type, data), { type, data });
  const work = page.locator('.turn.live').getByText('正在工作中......', { exact: true });
  for (const terminal of ['execution.completed', 'execution.failed', 'execution.cancelled']) {
    finished = false;
    failed = false;
    await page.goto(`${baseUrl}/c/${id}`);
    await page.waitForFunction(() => window.__ymirStreams?.length > 0);
    await emit('assistant.delta', { text: '正常回覆內容' });
    await emit('tool.started', {
      tool: 'bash',
      callId: 'c1',
      summary: 'npm install private-package',
    });
    await emit('status', { text: '工具執行中的額外狀態' });
    await emit('tool.started', { tool: 'read', callId: 'c2', summary: 'private-config.json' });
    await work.waitFor();
    assert.equal(await work.count(), 1);
    const text = await page.locator('.turn.live').innerText();
    for (const hidden of ['bash', 'npm install', 'private-config', '工具執行中的額外狀態'])
      assert.ok(!text.includes(hidden));
    assert.ok(text.includes('正常回覆內容'));
    assert.equal(await page.locator('.turn.live .tool, .turn.live code').count(), 0);
    await emit('tool.completed', { callId: 'c1', success: true });
    assert.equal(await work.count(), 1);
    await emit('tool.completed', { callId: 'c2', success: false });
    await work.waitFor({ state: 'detached' });
    await emit('tool.started', { tool: 'bash', callId: 'c3', summary: 'terminal command' });
    await work.waitFor();
    if (terminal === 'execution.completed') {
      await page.screenshot({ path: `${outDir}/01-working.png` });
      await page.reload();
      await page.waitForFunction(() => window.__ymirStreams?.length > 0);
      await emit('assistant.delta', { text: '正常回覆內容' });
      await emit('tool.started', { tool: 'bash', callId: 'c3', summary: 'terminal command' });
      await work.waitFor();
      assert.equal(await work.count(), 1);
    }
    finished = true;
    failed = terminal === 'execution.failed';
    await emit(
      terminal,
      terminal === 'execution.failed'
        ? { code: 'AGENT_RUNTIME_ERROR', message: '無法完成工作' }
        : { executionId, messageId: null },
    );
    await page.locator('.turn.live').waitFor({ state: 'detached' });
    assert.equal(await page.getByText('正在工作中......', { exact: true }).count(), 0);
    await page.locator('.turn.assistant').getByText('正常回覆內容', { exact: true }).waitFor();
    if (terminal === 'execution.failed')
      await page.getByText('無法完成工作', { exact: true }).waitFor();
  }
  assert.deepEqual(errors, []);
  console.log(
    'PASS one generic tool indicator, hidden command details, overlapping tools, streamed text, replay, completion, failure and cancellation.',
  );
} finally {
  await browser.close();
}
