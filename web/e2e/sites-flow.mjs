// 網站託管的端對端驗證（ADR-0016）：發布 → 公開瀏覽 → 改成指定使用者 → 票據登入 → 撤權 → 管理員可見。
// 前置：SQL Server；API（Development、Scripted harness 即可）加上
//         Ymir__Sites__BaseUrl=http://sites.localhost:5300  Ymir__Sites__Root=<網站目錄>
//         VibeMaker__Runtime__WorkspaceRoot=<工作目錄根>
//       SiteHost：dotnet run --project src/Ymir.SiteHost -- --urls=http://127.0.0.1:5300
//         --SiteHost:BaseUrl=http://sites.localhost:5300 --SiteHost:Root=<網站目錄>
//         --SiteHost:PlatformUrl=http://localhost:4200/ --ConnectionStrings:ymir=<連線字串>
//       ng serve。Chromium 會把 *.localhost 解析到本機，所以不需要 DNS。
// 用法：WORKSPACE_ROOT=<工作目錄根> node e2e/sites-flow.mjs <screenshot-dir> [baseUrl]
import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
const workspaceRoot = process.env.WORKSPACE_ROOT;
if (!workspaceRoot) throw new Error('WORKSPACE_ROOT is required');
mkdirSync(outDir, { recursive: true });
const browser = await chromium.launch(
  process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {},
);
const step = (name) => console.log(`✔ ${name}`);
const stamp = Date.now();

const devLogin = async (account, role = 'User') => {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const page = await context.newPage();
  page.on('dialog', (dialog) => void dialog.accept());
  await page.goto(`${baseUrl}/login`);
  await page.waitForSelector('details.dev');
  if (!(await page.locator('details.dev').evaluate((d) => d.open)))
    await page.click('details.dev summary');
  await page.fill('input[name=devAccount]', account);
  await page.selectOption('details.dev select[name=role]', role);
  await page.click('details.dev button[type=submit]');
  await page.waitForSelector('app-new-chat-page h1');
  return page;
};
const me = (page) => page.evaluate(() => fetch('/api/me').then((r) => r.json()));
// 網站頁的內容（SiteHost 回應的 HTML 或錯誤文字）
const bodyText = (page) => page.locator('body').innerText();

const owner = await devLogin(`site-owner-${stamp}`);
const friend = await devLogin(`site-friend-${stamp}`);
const stranger = await devLogin(`site-stranger-${stamp}`);
const admin = await devLogin(`site-admin-${stamp}`, 'Admin');
const friendName = (await me(friend)).displayName;

// 1. 開聊（建立工作目錄），把「建置好的網站」直接寫進工作目錄的 dist/
await owner.fill('app-composer textarea', '幫我做一個網站');
await owner.press('app-composer textarea', 'Enter');
await owner.waitForURL(/\/c\//);
await owner.waitForSelector('.turn.live', { state: 'detached', timeout: 60000 });
const conversationId = owner.url().split('/c/')[1].split(/[?#]/)[0];
const userId = (await me(owner)).id.replaceAll('-', '');
const dist = join(
  workspaceRoot,
  'users',
  userId,
  'workspace',
  'chats',
  conversationId.replaceAll('-', ''),
  'dist',
);
mkdirSync(dist, { recursive: true });
writeFileSync(
  join(dist, 'index.html'),
  '<!doctype html><title>Demo</title><h1>Hello from a Ymir site</h1>',
);
step('workspace has dist/index.html');

// 2. 從檔案面板發布
await owner.click('button.files-toggle');
await owner.getByRole('button', { name: '專案檔案', exact: true }).click();
const publish = owner.locator('app-publish-site');
await publish.locator('summary').click();
await publish.locator('input[name=siteName]').fill('示範網站');
await publish.locator('select[name=siteSource]').selectOption('dist');
await publish.locator('button[type=submit]').click();
await publish.locator('a[href*="sites.localhost"]').waitFor({ timeout: 30000 });
const siteUrl = await publish.locator('a[href*="sites.localhost"]').getAttribute('href');
step(`published ${siteUrl}`);

// 3. 公開：不需要登入（全新的 context）
const anonymous = await (await browser.newContext()).newPage();
await anonymous.goto(siteUrl);
assert.match(await bodyText(anonymous), /Hello from a Ymir site/);
step('public site is visible without login');

// 4. 改成「指定使用者」並分享給 friend
await owner.goto(`${baseUrl}/sites`);
const item = owner.locator('li[data-site="示範網站"]');
await item.locator('button:has-text("存取設定")').click();
const editor = item.locator('app-site-access-editor');
await editor.locator('input[value=SelectedUsers]').check();
await editor.locator('input[name=user-search]').fill(`site-friend-${stamp}`);
await editor.locator('button:has-text("搜尋")').click();
await editor.locator(`.results li:has-text("${friendName}") button:has-text("加入")`).click();
await editor.locator(`.chips li:has-text("${friendName}")`).waitFor();
await owner.screenshot({ path: `${outDir}/01-site-access-editor.png` });
await editor.locator('button:has-text("儲存存取設定")').click();
await owner.locator('[role=status]:has-text("指定使用者")').waitFor();
await owner.screenshot({ path: `${outDir}/02-sites-page.png` });
step('site shared with one user');

// SiteHost 的網站快取最多 10 秒
await new Promise((r) => setTimeout(r, 11000));

// 5. 未登入的瀏覽者：導向 Ymir 登入（新的 context：公開網站的回應可以被瀏覽器快取 60 秒）
const visitor = await (await browser.newContext()).newPage();
await visitor.goto(siteUrl);
await visitor.waitForURL(/\/login\?returnUrl=%2Fsite-access/);
step('anonymous visitor is sent to Ymir login');

// 6. friend：Ymir → 票據 → 網站（「分享給我的網站」也看得到）
await friend.goto(`${baseUrl}/sites`);
await friend.locator('li[data-shared-site="示範網站"]').waitFor();
await friend.screenshot({ path: `${outDir}/03-shared-with-me.png` });
await friend.goto(`${siteUrl}about?x=1`);
await friend.waitForURL(
  (url) => url.hostname.endsWith('.sites.localhost') && url.pathname === '/about',
);
await friend.goto(siteUrl);
assert.match(await bodyText(friend), /Hello from a Ymir site/);
step('shared user signs in with a one-time ticket');

// 7. stranger：沒有分享 → 平台顯示沒有權限
await stranger.goto(siteUrl);
await stranger.waitForURL(/\/site-access/);
await stranger.locator('app-site-access-page [role=alert]').waitFor();
await stranger.screenshot({ path: `${outDir}/04-not-shared.png` });
step('other users are refused');

// 8. 管理員一律可以看
await admin.goto(siteUrl);
await admin.waitForURL((url) => url.hostname.endsWith('.sites.localhost'));
assert.match(await bodyText(admin), /Hello from a Ymir site/);
step('Ymir admins can view private sites');

// 9. 撤權：移除 friend，授權快取（30 秒）到期後既有的網站 cookie 也失效
await item.locator('button:has-text("存取設定")').click();
await editor.locator(`.chips li:has-text("${friendName}") button`).click();
await editor.locator('button:has-text("儲存存取設定")').click();
await owner.locator('[role=status]:has-text("已更新")').waitFor();
await new Promise((r) => setTimeout(r, 31000));
await friend.goto(siteUrl);
assert.match(await bodyText(friend), /沒有權限/);
await friend.screenshot({ path: `${outDir}/05-revoked.png` });
step('revoked share takes effect');

await browser.close();
