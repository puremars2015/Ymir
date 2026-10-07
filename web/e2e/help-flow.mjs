// Development API with Scripted harness or Pi + Fake LLM; never use a paid model.
// Usage: node e2e/help-flow.mjs <outputDirectory> [baseUrl]
import assert from 'node:assert/strict';
import { mkdirSync } from 'node:fs';
import { chromium } from 'playwright';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const executablePath = process.env.CHROMIUM_PATH;
const browser = await chromium.launch(executablePath ? { executablePath } : {});
try {
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
  await page.goto(`${baseUrl}/login`);
  await page.locator('details.dev').waitFor();
  if (!(await page.locator('details.dev').evaluate((element) => element.open))) {
    await page.locator('details.dev summary').click();
  }
  await page.locator('input[name=devAccount]').fill(`help-${Date.now()}`);
  await page.locator('details.dev button[type=submit]').click();
  await page.locator('app-new-chat-page h1').waitFor();
  const input = page.locator('app-composer textarea');
  await input.fill('/');
  await page.locator('.help-hint').waitFor();
  await page.locator('.make-hint').waitFor();
  await page.screenshot({ path: `${outDir}/01-command-hints.png` });
  await input.fill('/he');
  assert.equal(await page.locator('.make-hint').count(), 0);
  await page.locator('.help-hint').click();
  await page.waitForFunction(() => document.querySelector('app-composer textarea')?.value === '/help');
  assert.equal(await input.inputValue(), '/help');
  await input.press('Enter');
  await page.waitForURL(/\/c\//);
  await page.locator('.turn.assistant:not(.live)').waitFor();
  await page.locator('.turn.live').waitFor({ state: 'detached' });
  assert.equal(await page.locator('.turn.user').innerText(), '/help');
  assert.equal(await page.locator('app-artifact-download a').count(), 0);
  const conversationId = new URL(page.url()).pathname.split('/').at(-1);
  const artifacts = await page.request.get(`${baseUrl}/api/conversations/${conversationId}/artifacts`);
  assert.equal(artifacts.status(), 200);
  assert.deepEqual(await artifacts.json(), []);
  await page.reload();
  await page.locator('.turn.assistant:not(.live)').waitFor();
  assert.equal(await page.locator('.turn.user').innerText(), '/help');
  await page.screenshot({ path: `${outDir}/02-help-history.png` });
  await page.setViewportSize({ width: 390, height: 844 });
  await input.fill('/h');
  await page.locator('.help-hint').waitFor();
  assert.equal(await page.locator('.make-hint').count(), 0);
  await page.screenshot({ path: `${outDir}/03-mobile-help-hint.png` });
  console.log('Help hints, Agent reply, original history, no deliverables, reload and mobile passed.');
} finally {
  await browser.close();
}
