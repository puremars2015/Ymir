// Isolated Development API with Scripted harness; do not run against production.
import assert from 'node:assert/strict';
import { mkdirSync } from 'node:fs';
import { chromium } from 'playwright';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const browser = await chromium.launch(
  process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {},
);
const errors = [];
async function login(page, name, role) {
  page.on('pageerror', (e) => errors.push(e.message));
  await page.goto(`${baseUrl}/login`);
  await page.locator('details.dev summary').click();
  await page.locator('input[name=devAccount]').fill(name);
  await page.locator('details.dev select').selectOption(role);
  await page.locator('details.dev button[type=submit]').click();
  await page.locator('app-new-chat-page h1').waitFor();
  return (await page.request.get(`${baseUrl}/api/me`)).json();
}
async function change(page, url, method, data) {
  const response = await page.evaluate(
    async ({ url, method, data }) => {
      const token = document.cookie
        .split('; ')
        .find((c) => c.startsWith('XSRF-TOKEN='))
        ?.slice('XSRF-TOKEN='.length);
      const r = await fetch(url, {
        method,
        headers: {
          'Content-Type': 'application/json',
          'X-XSRF-TOKEN': decodeURIComponent(token ?? ''),
        },
        body: data ? JSON.stringify(data) : undefined,
      });
      return { status: r.status, body: await r.text() };
    },
    { url, method, data },
  );
  assert.equal(response.status, 200, response.body);
}
try {
  const stamp = Date.now();
  const admin = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  const alice = await browser.newPage();
  const bob = await browser.newPage();
  await login(admin, `models-admin-${stamp}`, 'Admin');
  const user = await login(alice, `models-alice-${stamp}`, 'User');
  await login(bob, `models-bob-${stamp}`, 'User');
  const all = await (await admin.request.get(`${baseUrl}/api/admin/settings/models`)).json();
  assert.ok(
    all.models.length >= 4,
    'Configure at least four deployment models for this verification.',
  );
  const [a, b, c, d] = all.models;
  await change(admin, '/api/admin/settings/models', 'PUT', {
    enabledModelIds: [a.id, b.id, d.id],
    defaultModelId: a.id,
  });
  await admin.goto(`${baseUrl}/admin/users`);
  const row = admin.locator('tbody tr').filter({ hasText: user.accountName });
  await row.getByRole('button', { name: '模型權限', exact: true }).click();
  const card = admin.locator('app-user-model-card');
  await card
    .getByRole('combobox', { name: `${c.displayName} 模型權限`, exact: true })
    .selectOption('Allow');
  await card
    .getByRole('combobox', { name: `${d.displayName} 模型權限`, exact: true })
    .selectOption('Deny');
  await card.screenshot({ path: `${outDir}/01-user-model-overrides.png` });
  await card.getByRole('button', { name: '儲存模型權限', exact: true }).click();
  await card.waitFor({ state: 'detached' });
  assert.deepEqual(
    (await (await alice.request.get(`${baseUrl}/api/models`)).json()).map((m) => m.id),
    [a.id, b.id, c.id],
  );
  assert.deepEqual(
    (await (await bob.request.get(`${baseUrl}/api/models`)).json()).map((m) => m.id),
    [a.id, b.id, d.id],
  );
  const bobUser = await (await bob.request.get(`${baseUrl}/api/me`)).json();
  await admin
    .locator('tbody tr')
    .filter({ hasText: bobUser.accountName })
    .getByRole('button', { name: '模型權限', exact: true })
    .click();
  await card
    .getByRole('heading', { name: `${bobUser.displayName} 的模型權限`, exact: true })
    .waitFor();
  await card.getByRole('combobox').first().waitFor();
  assert.equal(
    await card
      .getByRole('combobox', { name: `${c.displayName} 模型權限`, exact: true })
      .inputValue(),
    'Inherit',
  );
  await row.getByRole('button', { name: '模型權限', exact: true }).click();
  await card
    .getByRole('heading', { name: `${user.displayName} 的模型權限`, exact: true })
    .waitFor();
  await card.getByRole('combobox').first().waitFor();
  assert.equal(
    await card
      .getByRole('combobox', { name: `${c.displayName} 模型權限`, exact: true })
      .inputValue(),
    'Allow',
  );
  await alice.reload();
  await alice.getByRole('button', { name: '模型與思考設定', exact: true }).click();
  await alice.getByRole('option').first().waitFor();
  assert.equal(await alice.getByRole('option').count(), 3);
  assert.ok(
    (await alice.getByRole('option').allTextContents()).some((n) => n.includes(c.displayName)),
  );
  assert.ok(
    !(await alice.getByRole('option').allTextContents()).some((n) => n.includes(d.displayName)),
  );
  await admin.reload();
  await row.getByRole('button', { name: '模型權限', exact: true }).click();
  await card.getByRole('combobox', { name: `${c.displayName} 模型權限`, exact: true }).waitFor();
  assert.equal(
    await card
      .getByRole('combobox', { name: `${c.displayName} 模型權限`, exact: true })
      .inputValue(),
    'Allow',
  );
  assert.equal(
    await card
      .getByRole('combobox', { name: `${d.displayName} 模型權限`, exact: true })
      .inputValue(),
    'Deny',
  );
  await card.getByRole('button', { name: '全部依系統設定', exact: true }).click();
  await card.waitFor({ state: 'detached' });
  await change(admin, '/api/admin/settings/models', 'PUT', {
    enabledModelIds: [b.id, c.id],
    defaultModelId: b.id,
  });
  assert.deepEqual(
    (await (await alice.request.get(`${baseUrl}/api/models`)).json()).map((m) => m.id),
    [b.id, c.id],
  );
  await row.getByRole('button', { name: '模型權限', exact: true }).click();
  await card.getByRole('combobox').first().waitFor();
  for (const select of await card.getByRole('combobox').all()) await select.selectOption('Deny');
  await card.getByText('沒有可使用的模型，這位使用者將無法送出新訊息。').waitFor();
  await card.getByRole('button', { name: '儲存模型權限', exact: true }).click();
  await card.waitFor({ state: 'detached' });
  assert.deepEqual(await (await alice.request.get(`${baseUrl}/api/models`)).json(), []);
  await row.getByRole('button', { name: '模型權限', exact: true }).click();
  await card.getByRole('combobox').first().waitFor();
  await admin.setViewportSize({ width: 390, height: 844 });
  await admin.waitForFunction(
    () => document.querySelector('.sidebar').getBoundingClientRect().right <= 1,
  );
  assert.ok((await card.boundingBox()).width <= 390);
  await card.screenshot({ path: `${outDir}/02-user-models-mobile.png` });
  assert.deepEqual(errors, []);
  console.log(
    'PASS real API inheritance, per-user grant/deny, picker, persistence, reset, changed defaults and deny-all.',
  );
} finally {
  await browser.close();
}
