// OneDrive 外掛的端對端驗證（ADR-0013、ADR-0030、ADR-0033）：管理員開放 → 使用者在外掛市集連結（經 Fake OIDC 授權）
// → 啟用「讓 Agent 使用」→（有 gateway 時）Agent 以工具搜尋 OneDrive → 對話執行後不會同步到 OneDrive → 解除連結。
// 前置：SQL Server、Fake OIDC（dotnet run --project tests/Ymir.Testing.FakeOidc，含 Fake Graph）；
//       API 設定 Ymir__Auth__Oidc__Authority=http://127.0.0.1:5299/00000000-0000-0000-0000-00000000f00d/v2.0、
//       Ymir__Auth__Oidc__ClientId=ymir-dev、Ymir__Auth__Oidc__ClientSecret=ymir-dev-secret、
//       Ymir__Connectors__OneDrive__GraphBaseUrl=http://127.0.0.1:5299/graph/v1.0；ng serve。
// 用法：node e2e/onedrive-flow.mjs <screenshot-dir> [baseUrl]
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const browser = await chromium.launch(
  process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {},
);
const step = (name) => console.log(`✔ ${name}`);
const stamp = Date.now();

const devLogin = async (page, account, role) => {
  await page.goto(`${baseUrl}/login`);
  await page.waitForSelector('details.dev');
  if (!(await page.locator('details.dev').evaluate((d) => d.open)))
    await page.click('details.dev summary');
  await page.fill('input[name=devAccount]', account);
  await page.selectOption('details.dev select[name=role]', role);
  await page.click('details.dev button[type=submit]');
  await page.waitForSelector('app-new-chat-page h1');
};

// 1. 管理員開放 OneDrive（全域預設）
const admin = await (
  await browser.newContext({ viewport: { width: 1280, height: 900 } })
).newPage();
await devLogin(admin, `od-admin-${stamp}`, 'Admin');
await admin.goto(`${baseUrl}/admin/settings`);
const card = admin.locator('app-extension-settings-card');
await card.locator('input[name=oneDrive]').check();
await card.locator('button:has-text("儲存")').click();
await card.locator('.summary:has-text("OneDrive 連結：允許")').waitFor();
await card.screenshot({ path: `${outDir}/01-admin-onedrive.png` });
step('admin enabled OneDrive for everyone');

// 2. 使用者連結 OneDrive（整頁導向 Fake OIDC，login_hint 自動通過）
const user = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
await devLogin(user, `od-user-${stamp}`, 'User');
await user.goto(`${baseUrl}/plugins`);
const plugin = user.locator('app-plugins-page li.card', { hasText: 'OneDrive' });
await plugin.locator('.status:has-text("尚未安裝")').waitFor();
const onedrive = user.locator('app-onedrive-card');
await onedrive.locator('text=尚未連結').waitFor();
await user.screenshot({ path: `${outDir}/02-not-connected.png`, fullPage: true });
await onedrive.locator('button:has-text("連結 OneDrive")').click();
await user.waitForURL(/\/plugins/, { timeout: 30000 });
await onedrive.locator('[role=status]:has-text("已連結 OneDrive")').waitFor();
await onedrive.locator(`text=od-user-${stamp}@fake-entra.test`).waitFor();
if (user.url().includes('onedrive='))
  throw new Error('result query should be removed from the URL');
step('user connected OneDrive through the authorization redirect');

// 2b. 外掛市集：啟用「讓 Agent 使用」需要確認風險（ADR-0030 §5）
await plugin.locator('.status:has-text("已安裝，未啟用")').waitFor();
user.once('dialog', (d) => {
  if (!d.message().includes('刪除')) throw new Error('enable confirmation should explain the risk');
  return d.accept();
});
await plugin.locator('input[type=checkbox]').check();
// 有設定 MCP gateway 時是「Agent 可以使用」，沒有時是「已啟用，暫時無法使用」並顯示原因。
await plugin.locator('.status', { hasText: /Agent 可以使用|已啟用/ }).waitFor();
await user.screenshot({ path: `${outDir}/02b-plugin-enabled.png`, fullPage: true });
step('user enabled the OneDrive plugin for the Agent');

// 3. 已連結：沒有同步資料夾設定（ADR-0033）
await onedrive.locator('strong:text-is("已連結")').waitFor();
if (await onedrive.locator('input[name=rootPath]').count())
  throw new Error('sync folder setting should be gone');
await user.screenshot({ path: `${outDir}/03-connected.png`, fullPage: true });
step('connected without a sync folder');

// 3b. Agent 自己使用 OneDrive 外掛（需要 API 的 VibeMaker__Harness=Pi、Ymir__Mcp__* 與 gateway 的 McpGateway__ApiUrl；
//     Fake LLM 的 [onedrive-search 關鍵字] 會呼叫 mcp__onedrive__search）。沒有設定 gateway 時略過。
if ((await plugin.locator('.status').innerText()).includes('Agent 可以使用')) {
  await user.goto(`${baseUrl}/`);
  await user.waitForSelector('app-new-chat-page h1');
  await user.fill('app-composer textarea', '[onedrive-search E2E] 找一下 E2E 資料夾');
  await user.press('app-composer textarea', 'Enter');
  await user
    .locator('.turn.assistant:has-text("MCP 回應")', { hasText: 'E2E' })
    .waitFor({ timeout: 90000 });
  await user.screenshot({ path: `${outDir}/03b-agent-searched-onedrive.png`, fullPage: true });
  step('the Agent searched the OneDrive through the plugin');
  await user.goto(`${baseUrl}/plugins`);
}

// 4. 對話執行後不同步到 OneDrive：檔案面板沒有雲端保存狀態（ADR-0033）
await user.goto(`${baseUrl}/`);
await user.waitForSelector('app-new-chat-page h1');
await user.fill('app-composer textarea', '幫我整理會議紀錄');
await user.press('app-composer textarea', 'Enter');
await user.locator('.turn.assistant').waitFor({ timeout: 30000 });
await user.click('button.files-toggle');
await user.waitForSelector('app-files-panel');
if (await user.locator('app-onedrive-sync').count())
  throw new Error('conversation files should not sync to OneDrive');
await user.screenshot({ path: `${outDir}/04-no-sync.png`, fullPage: true });
step('conversation files stay in Ymir (no OneDrive sync)');

// 5. 解除連結
await user.goto(`${baseUrl}/plugins`);
user.once('dialog', (d) => d.accept());
await onedrive.locator('button:has-text("解除連結")').click();
await onedrive.locator('text=尚未連結').waitFor();
step('disconnected');

await admin.goto(`${baseUrl}/admin/settings`);
await card.locator('input[name=oneDrive]').uncheck();
await card.locator('button:has-text("儲存")').click();
await card.locator('.summary:has-text("OneDrive 連結：不允許")').waitFor();
await browser.close();
