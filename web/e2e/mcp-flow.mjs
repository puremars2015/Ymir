// 平台 MCP 的端對端驗證（ADR-0012 B 階段 2）：管理員開放 echo 服務 → 成員在設定頁看到 → Agent 經 MCP Gateway 呼叫 echo。
// 前置（另見 deploy/mcp/README.md「本機驗證」）：SQL Server、Fake LLM、
//   FAKE_MCP_TOKEN=dev-backend-secret dotnet run --project tests/Ymir.Testing.FakeMcp
//   YMIR_MCP_ECHO_TOKEN=dev-backend-secret dotnet run --project src/Ymir.McpGateway -- --urls http://127.0.0.1:5310 \
//     --McpGateway:CatalogPath=deploy/mcp/servers.example.json --McpGateway:TokenSigningKey=<key> --ConnectionStrings:ymir=<同 API>
//   API：VibeMaker__Harness=Pi、Ymir__Mcp__GatewayUrl=http://127.0.0.1:5310、Ymir__Mcp__CatalogPath、Ymir__Mcp__TokenSigningKey；ng serve。
// 用法：node e2e/mcp-flow.mjs <screenshot-dir> [baseUrl]
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

// 1. 管理員開放 echo 給所有使用者
const admin = await (
  await browser.newContext({ viewport: { width: 1280, height: 900 } })
).newPage();
await devLogin(admin, `mcp-admin-${stamp}`, 'Admin');
await admin.goto(`${baseUrl}/admin/settings`);
const card = admin.locator('app-platform-mcp-card');
const echo = card.locator('[data-server="echo"]');
await echo.waitFor();
await echo.locator('input[type=checkbox]').first().check();
await echo.locator('select').selectOption('Everyone');
await echo.locator('button:has-text("儲存")').click();
await card.locator('[role=status]:has-text("所有使用者可用")').waitFor();
await card.screenshot({ path: `${outDir}/01-admin-platform-mcp.png` });
step('admin enabled the echo platform service for everyone');

// 2. 成員在設定頁看到可用的平台服務
const user = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
await devLogin(user, `mcp-user-${stamp}`, 'User');
await user.goto(`${baseUrl}/settings`);
await user.locator('app-my-extensions-card li:has-text("平台服務：") strong:has-text("echo")').waitFor();
step('member sees the platform service');

// 3. Agent 經 gateway 呼叫 echo
await user.goto(`${baseUrl}/`);
await user.waitForSelector('app-new-chat-page h1');
await user.fill('app-composer textarea', '[mcp-echo] 請呼叫平台服務');
await user.press('app-composer textarea', 'Enter');
await user.locator('.turn.assistant:has-text("echo: ping from agent")').waitFor({ timeout: 60000 });
await user.screenshot({ path: `${outDir}/02-agent-called-platform-mcp.png`, fullPage: true });
step('agent called the platform tool through the MCP Gateway');

// 收尾：停用
await echo.locator('input[type=checkbox]').first().uncheck();
await echo.locator('button:has-text("儲存")').click();
await card.locator('[role=status]:has-text("停用")').waitFor();
await browser.close();
