// 管理介面的端對端驗證（ADR-0010、ADR-0011、ADR-0012）：總覽、停止執行環境、稽核紀錄、系統設定（Entra ID、Tunnel、執行政策、擴充能力）、用量。
// 前置：SQL Server、Fake LLM（FAKE_LLM_MASTER_KEY=sk-dev，模擬 LiteLLM）、Fake OIDC（dotnet run --project tests/Ymir.Testing.FakeOidc）都已啟動；
//       API 另設 VibeMaker__LiteLlm__BaseUrl=http://127.0.0.1:5199/、VibeMaker__LiteLlm__MasterKey=sk-dev（第 13 步：用量與每月預算）；
//       API 以 VibeMaker__Harness=Pi、Ymir__Auth__Oidc__AuthorityHost=http://127.0.0.1:5299 啟動（不設定 Ymir__Auth__Oidc__Authority，
//       Entra 由管理介面設定）；ng serve。
// 用法：node e2e/admin-flow.mjs <screenshot-dir> [baseUrl]
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const executablePath = process.env.CHROMIUM_PATH;
const browser = await chromium.launch(executablePath ? { executablePath } : {});
const step = (name) => console.log(`✔ ${name}`);
const stamp = Date.now();

const devLogin = async (page, account, role) => {
  await page.goto(`${baseUrl}/login`);
  await page.waitForSelector('details.dev');
  if (!(await page.locator('details.dev').evaluate((d) => d.open))) {
    await page.click('details.dev summary');
  }
  await page.fill('input[name=devAccount]', account);
  await page.selectOption('details.dev select[name=role]', role);
  await page.click('details.dev button[type=submit]');
  await page.waitForSelector('app-new-chat-page h1');
};

// 1. 一般使用者送一則訊息，建立自己的執行環境
const worker = await (await browser.newContext()).newPage();
const workerAccount = `worker-${stamp}`;
await devLogin(worker, workerAccount, 'User');
await worker.fill('app-composer textarea', '[create-file] 管理介面測試');
await worker.press('app-composer textarea', 'Enter');
await worker.waitForURL(/\/c\//);
await worker.waitForSelector('.turn.assistant:has-text("已完成")', { timeout: 60000 });
await worker.waitForSelector('.turn.live', { state: 'detached', timeout: 60000 });
if ((await worker.locator('.user a:has-text("管理")').count()) !== 0)
  throw new Error('regular user must not see the admin link');
step('regular user ran the agent (runtime created, no admin link)');

// 2. Admin 進入總覽：統計卡片、近 7 天長條圖、執行環境清單
const admin = await (
  await browser.newContext({ viewport: { width: 1280, height: 900 } })
).newPage();
admin.on('dialog', (d) => d.accept());
await devLogin(admin, `admin-${stamp}`, 'Admin');
await admin.click('.user a:has-text("管理")');
await admin.waitForURL(/\/admin$/);
await admin.waitForSelector('app-overview-page .stat:has-text("今日執行")');
const bars = await admin.locator('app-overview-page .bar-col').count();
if (bars !== 7) throw new Error(`expected 7 trend bars, got ${bars}`);
const row = admin.locator(`app-overview-page tr:has-text("${workerAccount}")`);
await row.waitFor();
await admin.screenshot({ path: `${outDir}/01-overview.png`, fullPage: true });
step('overview shows stats, 7-day trend and the worker runtime');

// 3. 停止使用者的執行環境
await row.locator('button:has-text("停止")').click();
await admin.waitForSelector('app-overview-page [role=status]:has-text("已停止")');
await row.locator('.badge:has-text("已停止")').waitFor();
if ((await row.locator('button:has-text("停止")').count()) !== 0)
  throw new Error('stopped runtime still offers stop');
step('stopped the worker runtime');

// 4. 從總覽跳到該使用者的稽核紀錄，看到剛才的停止操作與使用者的登入
await row.locator('a:has-text("稽核紀錄")').click();
await admin.waitForURL(/\/admin\/audit\?/);
await admin.waitForSelector(`app-audit-page .chip:has-text("${workerAccount}")`);
await admin.waitForSelector('app-audit-page tr:has-text("停止執行環境")');
await admin.waitForSelector('app-audit-page tr:has-text("開發登入")');
await admin.waitForSelector('app-audit-page tr:has-text("送出訊息")');
await admin.screenshot({ path: `${outDir}/02-audit-user.png`, fullPage: true });
step('audit page filtered by the worker shows stop, login and executions');

// 5. 篩選「管理操作」：只剩 admin.* 紀錄
await admin.selectOption('app-audit-page select[name=action]', 'admin.');
await admin.click('app-audit-page form button[type=submit]');
await admin.waitForFunction(() => {
  const rows = [...document.querySelectorAll('app-audit-page tbody tr code')];
  return rows.length > 0 && rows.every((c) => c.textContent.startsWith('admin.'));
});
step('action filter narrows to admin operations');

// 6. 清除使用者篩選後看到所有人的紀錄；窄螢幕截圖
await admin.click('app-audit-page .chip button');
await admin.waitForSelector('app-audit-page .chip', { state: 'detached' });
await admin.waitForSelector('app-audit-page tbody tr code');
await admin.setViewportSize({ width: 390, height: 800 });
await admin.click('app-admin-tabs a:has-text("總覽")');
await admin.waitForSelector('app-overview-page .stat');
await admin.screenshot({ path: `${outDir}/03-overview-mobile.png`, fullPage: true });
step('cleared the user filter; overview on a narrow screen');

// 7. 系統設定：Entra ID（Fake OIDC 模擬）。先確保有本機 Admin（停用 / 還原企業帳號登入的防鎖死條件）
await admin.setViewportSize({ width: 1280, height: 900 });
const localAdmin = `ladmin${stamp}`.slice(0, 30);
await admin.click('app-admin-tabs a:has-text("使用者")');
await admin.waitForSelector('app-users-page table');
await admin.click('app-users-page header button');
await admin.fill('input[name=account]', localAdmin);
await admin.fill('input[name=displayName]', '本機管理員');
await admin.selectOption('form.create select[name=role]', 'Admin');
await admin.fill('input[name=password]', 'local-admin-password-123');
await admin.click('form.create button[type=submit]');
await admin.waitForSelector(`app-users-page [role=status]:has-text("${localAdmin}")`);

await admin.click('app-admin-tabs a:has-text("系統設定")');
await admin.waitForSelector('app-settings-page input[name=tenantId]');
if (await admin.locator('app-settings-page button:has-text("還原為部署設定")').count()) {
  // 之前的執行留下的設定：先還原，從未設定的狀態開始
  await admin.click('app-settings-page button:has-text("還原為部署設定")');
  await admin.waitForSelector('app-settings-page [role=status]:has-text("已還原")');
}
await admin.click('app-admin-tabs a:has-text("總覽")');
await admin.click('app-overview-page .warning:has-text("尚未設定企業帳號")');
await admin.waitForURL(/\/admin\/settings/);
step('overview warns that Entra is not configured and links to settings');

const soon = new Date();
soon.setDate(soon.getDate() + 10);
const soonText = `${soon.getFullYear()}-${String(soon.getMonth() + 1).padStart(2, '0')}-${String(soon.getDate()).padStart(2, '0')}`;
await admin.fill('input[name=tenantId]', '00000000-0000-0000-0000-00000000f00d');
await admin.fill('input[name=clientId]', 'ymir-dev');
await admin.fill('input[name=clientSecret]', 'ymir-dev-secret');
await admin.fill('input[name=secretExpiresOn]', soonText);
await admin.fill('input[name=displayName]', '測試公司帳號');
await admin.click('app-settings-page button:has-text("測試設定")');
await admin.waitForSelector('app-settings-page .test.ok');
await admin.click('app-settings-page form button[type=submit]');
await admin.waitForSelector('app-settings-page [role=status]:has-text("立即使用新設定")');
if ((await admin.inputValue('input[name=clientSecret]')) !== '')
  throw new Error('secret field must be cleared after saving');
await admin.waitForSelector('app-settings-page input[name=clientSecret][placeholder*="已設定"]');
await admin.screenshot({ path: `${outDir}/04-settings-entra.png`, fullPage: true });
step('saved Entra settings (test passed, secret not shown again)');

await admin.click('app-admin-tabs a:has-text("總覽")');
await admin.waitForSelector('app-overview-page .warning:has-text("天後到期")');
step('overview warns about the expiring secret');

// 8. 不重啟：新的瀏覽器直接用企業帳號登入
const sso = await (await browser.newContext()).newPage();
await sso.goto(`${baseUrl}/login`);
await sso.click('a.sso:has-text("以測試公司帳號登入")');
await sso.waitForSelector('input[name=account]');
await sso.fill('input[name=account]', `entra-${stamp}`);
await sso.fill('input[name=display_name]', 'Entra 使用者');
await sso.click('button[type=submit]');
await sso.waitForSelector('.user .name:has-text("Entra 使用者")');
step('signed in with the company account configured from the admin console (no restart)');

// 9. 還原為部署設定 → 登入頁不再有企業帳號按鈕
await admin.click('app-admin-tabs a:has-text("系統設定")');
await admin.click('app-settings-page button:has-text("還原為部署設定")');
await admin.waitForSelector('app-settings-page [role=status]:has-text("已還原")');
const after = await (await browser.newContext()).newPage();
await after.goto(`${baseUrl}/login`);
await after.waitForSelector('input[name=account]');
if (await after.locator('a.sso').count())
  throw new Error('company login still offered after reset');
step('reset to deployment settings removes the company login');

// 10. 系統設定：Cloudflare Tunnel。開發環境沒有 runtime host → token 欄位停用；對外網域可以設定與還原
await admin.click('app-admin-tabs a:has-text("系統設定")');
const tunnelCard = admin.locator('app-tunnel-settings-card');
await tunnelCard.locator('.state:has-text("此部署不支援")').waitFor();
if (!(await tunnelCard.locator('input[name=tunnelToken]').isDisabled()))
  throw new Error('token input must be disabled without a runtime host');
await tunnelCard.locator('input[name=hostname]').fill('https://bad.example.com');
await tunnelCard.locator('text=請輸入網域名稱').waitFor();
await tunnelCard.locator('input[name=hostname]').fill('ymir.e2e-example.com');
await tunnelCard.locator('button:has-text("儲存網域")').click();
await tunnelCard.locator('[role=status]:has-text("ymir.e2e-example.com")').waitFor();
await tunnelCard.locator('code:has-text("https://ymir.e2e-example.com/signin-oidc")').waitFor();
await admin.screenshot({ path: `${outDir}/05-settings-tunnel.png`, fullPage: true });
await tunnelCard.locator('input[name=hostname]').fill('');
await tunnelCard.locator('button:has-text("儲存網域")').click();
await tunnelCard.locator('[role=status]:has-text("已還原")').waitFor();
step('tunnel card: unavailable without runtime host; public hostname set and reset');

// 11. 執行政策：每日上限設為 1 → 已執行過一次的使用者再送出會被拒絕（429），不必重啟
const runtimeCard = admin.locator('app-runtime-settings-card');
await runtimeCard.locator('.summary:has-text("閒置")').waitFor();
await runtimeCard.locator('input[name=executionTimeoutMinutes]').fill('0');
await runtimeCard.locator('text=單次執行上限必須是').waitFor();
await runtimeCard.locator('input[name=executionTimeoutMinutes]').fill('20');
await runtimeCard.locator('input[name=idleTimeoutMinutes]').fill('45');
await runtimeCard.locator('input[name=dailyExecutionLimit]').fill('1');
await runtimeCard.locator('button:has-text("儲存")').click();
await runtimeCard.locator('[role=status]:has-text("立即生效")').waitFor();
await runtimeCard.locator('.summary:has-text("每日 1 次")').waitFor();
await runtimeCard.screenshot({ path: `${outDir}/06-settings-runtime.png` });
await worker.fill('app-composer textarea', '再做一次');
await worker.press('app-composer textarea', 'Enter');
await worker.waitForSelector('.composer-area .error:has-text("每日執行次數上限")');
await worker.screenshot({ path: `${outDir}/07-quota-exceeded.png` });
step('runtime policy saved; daily limit rejects the next message without a restart');

// 12. 用量：使用者的執行次數，24 小時次數標示已達上限；之後還原執行政策
await admin.click('app-admin-tabs a:has-text("用量")');
const workerRow = admin.locator(`app-usage-page tr:has-text("${workerAccount}")`);
await workerRow.locator('.quota[data-level="reached"]').waitFor();
await admin.selectOption('app-usage-page select[name=days]', { label: '近 30 天' });
await workerRow.waitFor();
await admin.screenshot({ path: `${outDir}/08-usage.png`, fullPage: true });
await admin.click('app-admin-tabs a:has-text("系統設定")');
await admin.locator('app-runtime-settings-card button:has-text("還原為部署設定")').click();
await admin.locator('app-runtime-settings-card [role=status]:has-text("已還原")').waitFor();
step('usage page shows the worker at the daily limit; policy reset to deployment');

// 13. 每月模型預算（LiteLLM，Fake LLM 每次呼叫 US$0.01）：設為 US$0.02 → 已用過的使用者被擋下；用量頁顯示費用與預算
await runtimeCard.locator('input[name=monthlyBudgetUsd]').fill('1.234');
await runtimeCard.locator('text=最多兩位小數').waitFor();
await runtimeCard.locator('input[name=monthlyBudgetUsd]').fill('0.02');
await runtimeCard.locator('button:has-text("儲存")').click();
await runtimeCard.locator('.summary:has-text("每月預算 US$0.02")').waitFor();
const budgetMessage = worker.locator(
  '.composer-area .error:has-text("預算"), .turn.error-message:has-text("預算")',
);
for (let i = 0; i < 4 && !(await budgetMessage.count()); i++) {
  await worker.fill('app-composer textarea', `預算測試 ${i}`);
  await worker.press('app-composer textarea', 'Enter');
  await Promise.race([
    budgetMessage.first().waitFor({ timeout: 60000 }),
    worker.waitForSelector('.turn.live', { state: 'detached', timeout: 60000 }),
  ]);
  await worker.waitForTimeout(500);
}
await budgetMessage.first().waitFor();
await worker.screenshot({ path: `${outDir}/09-budget-exceeded.png` });
await admin.click('app-admin-tabs a:has-text("用量")');
await admin.locator('app-usage-page th:has-text("費用")').waitFor();
await workerRow.locator('.quota[data-level="reached"]:has-text("US$0.02")').waitFor();
await admin.screenshot({ path: `${outDir}/10-usage-model.png`, fullPage: true });
await admin.click('app-admin-tabs a:has-text("系統設定")');
await admin.locator('app-runtime-settings-card button:has-text("還原為部署設定")').click();
await admin.locator('app-runtime-settings-card [role=status]:has-text("已還原")').waitFor();
step('monthly model budget enforced; usage page shows LiteLLM spend, tokens and budget');

// 14. Agent 擴充能力（ADR-0012）：全域預設開放 skill；使用者頁對 worker 個人開放 MCP；worker 的個人設定頁看到結果
const extensionCard = admin.locator('app-extension-settings-card');
await extensionCard.locator('input[name=skills]').check();
await extensionCard.locator('button:has-text("儲存")').click();
await extensionCard.locator('[role=status]:has-text("下一則訊息")').waitFor();
await extensionCard.locator('.summary:has-text("自建 skill：允許")').waitFor();
await extensionCard.screenshot({ path: `${outDir}/11-settings-extensions.png` });
await admin.click('app-admin-tabs a:has-text("使用者")');
await admin.fill('app-users-page input[name=search]', workerAccount);
await admin.click('app-users-page .search button');
const workerUserRow = admin.locator(`app-users-page tr:has-text("${workerAccount}")`).first();
await workerUserRow.locator('button:has-text("擴充能力")').click();
const extensionForm = admin.locator('app-users-page .extension-form');
await extensionForm
  .locator('select[name=skills] option:has-text("依全域預設（允許）")')
  .waitFor({ state: 'attached' });
await extensionForm.locator('select[name=mcp]').selectOption('Allow');
await admin.screenshot({ path: `${outDir}/12-user-extensions.png`, fullPage: true });
await extensionForm.locator('button:has-text("儲存")').click();
await admin.locator('app-users-page [role=status]:has-text("擴充能力")').waitFor();
await worker.goto(`${baseUrl}/settings`);
await worker
  .locator('app-my-extensions-card li:has-text("自建 skill") strong:has-text("已開放")')
  .waitFor();
await worker
  .locator('app-my-extensions-card li:has-text("自建 MCP server") strong:has-text("已開放")')
  .waitFor();
await worker.screenshot({ path: `${outDir}/13-my-extensions.png`, fullPage: true });
await admin.click('app-admin-tabs a:has-text("系統設定")');
await extensionCard.locator('input[name=skills]').uncheck();
await extensionCard.locator('button:has-text("儲存")').click();
await extensionCard.locator('.summary:has-text("自建 skill：不允許")').waitFor();
step('extension policy: global default and per-user override saved; member sees the result');

await browser.close();
