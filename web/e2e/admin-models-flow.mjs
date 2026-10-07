// Admin model controls with isolated, stateful API fixtures; no production settings are written.
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';
import assert from 'node:assert/strict';
const [outDir = '.', baseUrl = 'http://localhost:4301'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH });
const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
let enabled = ['one', 'two'],
  defaultId = 'one',
  usesDeployment = true;
let saves = 0;
const models = [
  { id: 'one', displayName: 'MiniMax-M2.7', supportsImages: false },
  { id: 'two', displayName: 'Claude Sonnet 5.5 (OpenRouter)', supportsImages: true },
];
const state = () => ({
  models: models.map((m) => ({ ...m, enabled: enabled.includes(m.id) })),
  defaultModelId: defaultId,
  usesDeployment,
  updatedAt: usesDeployment ? null : '2026-10-07T10:20:00Z',
});
await context.route('**/api/**', async (route) => {
  const path = new URL(route.request().url()).pathname.replace(/\/$/, '');
  const method = route.request().method();
  if (path === '/api/admin/settings/models') {
    if (method === 'PUT') {
      const data = route.request().postDataJSON();
      assert.deepEqual(data, { enabledModelIds: ['two'], defaultModelId: 'two' });
      enabled = data.enabledModelIds;
      defaultId = data.defaultModelId;
      usesDeployment = false;
      saves++;
    } else if (method === 'DELETE') {
      enabled = ['one', 'two'];
      defaultId = 'one';
      usesDeployment = true;
    } else assert.equal(method, 'GET');
    await route.fulfill({ json: state() });
    return;
  }
  assert.equal(method, 'GET');
  const data =
    path === '/api/me'
      ? {
          id: '11111111-1111-4111-8111-111111111111',
          displayName: 'Admin',
          role: 'Admin',
          status: 'Active',
          authMethod: 'Password',
          mustChangePassword: false,
        }
      : path === '/api/models'
        ? models
            .filter((m) => enabled.includes(m.id))
            .map((m) => ({ ...m, isDefault: m.id === defaultId }))
        : ['/api/projects', '/api/conversations', '/api/make-topics'].includes(path)
          ? []
          : null;
  if (data !== null) await route.fulfill({ json: data });
  else await route.fulfill({ status: 503, json: { detail: '此驗證未載入其他設定卡片。' } });
});
const page = await context.newPage();
const errors = [];
page.on('pageerror', (e) => errors.push(e.message));
const card = page.locator('app-model-settings-card');
try {
  await page.goto(`${baseUrl}/admin/settings`);
  const one = card.getByRole('checkbox', { name: 'MiniMax-M2.7', exact: true });
  const two = card.getByRole('checkbox', { name: 'Claude Sonnet 5.5', exact: true });
  await one.waitFor();
  await page.waitForFunction(
    () => document.querySelectorAll('app-model-settings-card input[type=checkbox]')[1]?.checked,
  );
  assert.equal(await one.isChecked(), true);
  assert.equal(await two.isChecked(), true);
  await one.uncheck();
  await two.uncheck();
  await page.waitForFunction(
    () => document.querySelector('app-model-settings-card button[type=submit]')?.disabled,
  );
  assert.equal(
    await card.getByRole('button', { name: '儲存模型設定', exact: true }).isDisabled(),
    true,
  );
  await two.check();
  await page.waitForFunction(
    () => document.querySelector('app-model-settings-card select')?.value === 'two',
  );
  assert.equal(await card.getByRole('combobox').inputValue(), 'two');
  await card.getByRole('button', { name: '儲存模型設定', exact: true }).click();
  await card.getByText('模型設定已儲存。', { exact: true }).waitFor();
  assert.equal(saves, 1);
  assert.equal(await one.isChecked(), false);
  await card.screenshot({ path: `${outDir}/admin-models.png` });
  await page.reload();
  await one.waitFor();
  await page.waitForFunction(
    () => document.querySelectorAll('app-model-settings-card input[type=checkbox]')[1]?.checked,
  );
  assert.equal(await one.isChecked(), false);
  await page.goto(baseUrl);
  await page.getByRole('button', { name: '模型與思考設定', exact: true }).click();
  await page.getByRole('listbox', { name: '模型', exact: true }).waitFor();
  assert.equal(await page.getByRole('option').count(), 1);
  assert.equal(await page.getByRole('option').innerText(), 'Claude Sonnet 5.5');
  await page.keyboard.press('Escape');
  await page.goto(`${baseUrl}/admin/settings`);
  await card.getByRole('button', { name: '還原模型部署設定', exact: true }).click();
  await card.getByText('模型設定已儲存。', { exact: true }).waitFor();
  await page.waitForFunction(
    () => document.querySelectorAll('app-model-settings-card input[type=checkbox]')[0]?.checked,
  );
  assert.equal(await one.isChecked(), true);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForFunction(
    () => document.querySelector('.sidebar').getBoundingClientRect().right <= 1,
  );
  await card.screenshot({ path: `${outDir}/admin-models-mobile.png` });
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
  assert.deepEqual(errors, []);
  console.log(
    'PASS model toggles, empty-set guard, default selection, save/reload, user picker refresh, reset and mobile layout',
  );
} finally {
  await browser.close();
}
