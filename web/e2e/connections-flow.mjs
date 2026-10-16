// ADR-0028：只用隔離的 Development API／資料庫，不能指向正式站。
// node e2e/connections-flow.mjs <screenshot-dir> [baseUrl]
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';
import assert from 'node:assert/strict';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
assert.ok(
  ['localhost', '127.0.0.1'].includes(new URL(baseUrl).hostname),
  'Local test environment only',
);
mkdirSync(outDir, { recursive: true });
const browser = await chromium.launch(
  process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {},
);
const stamp = Date.now();
const errors = [];
async function login(account, role) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const page = await context.newPage();
  page.on('pageerror', (error) => errors.push(error.message));
  await page.goto(`${baseUrl}/login`);
  await page.locator('details.dev summary').click();
  await page.fill('input[name=devAccount]', account);
  await page.selectOption('details.dev select[name=role]', role);
  await page.click('details.dev button[type=submit]');
  await page.waitForSelector('app-new-chat-page h1');
  return page;
}
async function grant(admin, userId, effect) {
  const status = await admin.evaluate(
    async ({ userId, effect }) => {
      const token = document.cookie
        .split('; ')
        .find((c) => c.startsWith('XSRF-TOKEN='))
        ?.slice('XSRF-TOKEN='.length);
      const response = await fetch(`/api/admin/users/${userId}/extensions`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
          'X-XSRF-TOKEN': decodeURIComponent(token ?? ''),
        },
        body: JSON.stringify({
          skills: 'Inherit',
          mcp: effect,
          internet: 'Allow',
          oneDrive: 'Inherit',
        }),
      });
      return response.status;
    },
    { userId, effect },
  );
  assert.equal(status, 200);
}
try {
  const admin = await login(`connections-admin-${stamp}`, 'Admin');
  const user = await login(`connections-user-${stamp}`, 'User');
  const me = await (await user.request.get(`${baseUrl}/api/me`)).json();
  // 舊網址導向外掛市集（ADR-0030）；OneDrive 卡片與個人 MCP 都在同一頁。
  await user.goto(`${baseUrl}/settings/connections`);
  await user.waitForURL(/\/plugins$/);
  await user.getByRole('link', { name: '外掛', exact: true }).waitFor();
  await user.locator('app-plugins-page li.card h2:has-text("OneDrive")').waitFor();
  const card = user.locator('app-personal-mcp-card');
  await card.getByText('尚未註冊個人 MCP。').waitFor();
  assert.equal(await card.locator('form').count(), 0);
  await grant(admin, me.id, 'Allow');
  await user.reload();
  await card.locator('input[name=mcpName]').fill('browser-tools');
  await card.locator('input[name=mcpUrl]').fill('https://tools.example.invalid/mcp');
  await card.locator('input[name=mcpToken]').fill('browser-fixture-token');
  const added = user.waitForResponse(
    (r) => r.url().endsWith('/api/connections/mcp') && r.request().method() === 'POST',
  );
  await card.getByRole('button', { name: '新增並啟用 MCP' }).click();
  assert.ok(!(await (await added).text()).includes('browser-fixture-token'));
  await card.locator('li strong', { hasText: 'browser-tools' }).waitFor();
  assert.equal(await card.locator('input[name=mcpToken]').inputValue(), '');
  await card.getByRole('button', { name: '測試連線', exact: true }).click();
  await card
    .getByText('連線測試失敗；請檢查服務、憑證與公開網路設定。')
    .waitFor({ timeout: 20000 });
  await card.getByRole('button', { name: '停用', exact: true }).click();
  await card.getByRole('button', { name: '啟用', exact: true }).waitFor();
  await card.getByRole('button', { name: '啟用', exact: true }).click();
  await card.getByRole('button', { name: '停用', exact: true }).waitFor();
  await user.screenshot({ path: `${outDir}/connections-desktop.png`, fullPage: true });
  await grant(admin, me.id, 'Deny');
  await user.reload();
  await card.getByRole('button', { name: '移除', exact: true }).waitFor();
  assert.equal(await card.locator('form').count(), 0);
  assert.equal(
    await card.getByRole('button', { name: '測試連線', exact: true }).isEnabled(),
    false,
  );
  await card.getByRole('button', { name: '移除', exact: true }).click();
  await card.getByText('尚未註冊個人 MCP。').waitFor();
  await user.goto(`${baseUrl}/settings?onedrive=denied`);
  await user.waitForURL(/\/plugins/);
  await user.getByText('已取消連結 OneDrive。', { exact: true }).waitFor();
  await user.setViewportSize({ width: 390, height: 844 });
  await user.screenshot({ path: `${outDir}/connections-mobile.png`, fullPage: true });
  assert.deepEqual(errors, []);
  console.log(
    'Connections E2E passed: navigation, permission, registration/redaction, bounded probe failure, toggle, revocation/removal, legacy OAuth return, mobile.',
  );
} finally {
  await browser.close();
}
