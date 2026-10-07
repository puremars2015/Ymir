// 對外連線管制的端對端驗證（ADR-0012 A.8）：管理員關閉某位成員的對外連線 → 下一次執行時 container 以受限網路重建，
// Agent 仍連得到模型、連不到外網、Pi session 保留；恢復後重建回一般網路，並寫稽核 runtime.recreate。
// 前置（需要 Podman，Local runtime 無法限制網路）：
//   podman network create ymir-agents-net && podman network create --internal ymir-agents
//   模型端點以 container 執行並接上兩個 network，名稱為 litellm（沙箱以 Fake LLM：
//     podman run -d --name litellm --network ymir-agents-net --network ymir-agents \
//       -v $PWD/tests/Ymir.Testing.FakeLlm/bin/Debug/net10.0:/app:ro -e ASPNETCORE_URLS=http://0.0.0.0:5199 \
//       mcr.microsoft.com/dotnet/aspnet:10.0 dotnet /app/Ymir.Testing.FakeLlm.dll）
//   API：VibeMaker__Harness=Pi、VibeMaker__Runtime__Provider=Podman（root Podman 的沙箱用 Docker + ContainerExecutable=podman）、
//        VibeMaker__Runtime__Network=ymir-agents-net、VibeMaker__Runtime__RestrictedNetwork=ymir-agents、
//        VibeMaker__Pi__ModelBaseUrl=http://litellm:5199/v1；ng serve。
// 用法：node e2e/network-flow.mjs <screenshot-dir> [baseUrl]
import { chromium } from 'playwright';
import { execFileSync } from 'node:child_process';
import { mkdirSync } from 'node:fs';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH });
const step = (name) => console.log(`✔ ${name}`);
const stamp = Date.now();
const podman = (...args) => execFileSync('podman', args, { encoding: 'utf8' }).trim();

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

const send = async (page, text) => {
  const before = await page.locator('.turn.assistant').count();
  await page.fill('app-composer textarea', text);
  await page.press('app-composer textarea', 'Enter');
  await page.waitForURL(/\/c\//);
  await page.waitForFunction(
    (n) => document.querySelectorAll('.turn.assistant').length > n,
    before,
    { timeout: 90000 },
  );
  await page.waitForSelector('.turn.live', { state: 'detached', timeout: 90000 });
  const last = (await page.locator('.turn.assistant').allInnerTexts()).at(-1) ?? '';
  if (/錯誤|失敗/.test(last)) throw new Error(`agent failed: ${last}`);
  return last;
};

const worker = await (await browser.newContext()).newPage();
const account = `net-${stamp}`;
await devLogin(worker, account, 'User');
await send(worker, '第一則（可以對外連線）');
const me = await worker.evaluate(async () => (await (await fetch('/api/me')).json()).id);
const container = `ymir-user-${me.replaceAll('-', '')}`;
const networkOf = () =>
  podman(
    'container',
    'inspect',
    '--format',
    '{{range $k, $v := .NetworkSettings.Networks}}{{$k}} {{end}}|{{index .Config.Labels "ymir.network"}}',
    container,
  );
step(`internet: ${networkOf()}`);

const admin = await (
  await browser.newContext({ viewport: { width: 1280, height: 900 } })
).newPage();
await devLogin(admin, `net-admin-${stamp}`, 'Admin');
await admin.goto(`${baseUrl}/admin/users`);
await admin.fill('app-users-page input[name=search]', account);
await admin.click('app-users-page .search button');
const row = admin.locator(`app-users-page tr:has-text("${account}")`).first();
await row.locator('button:has-text("擴充能力")').click();
await admin.locator('app-users-page .extension-form select[name=internet]').selectOption('Deny');
await admin.locator('app-users-page .extension-form button:has-text("儲存")').click();
await admin.locator('app-users-page [role=status]:has-text("擴充能力")').waitFor();
await admin.goto(`${baseUrl}/admin/settings`);
await admin.locator('app-extension-settings-card input[name=internet]').waitFor();
await admin
  .locator('app-extension-settings-card')
  .screenshot({ path: `${outDir}/14-settings-internet.png` });

const restrictedReply = await send(worker, '第二則（對外連線已關閉）');
const restricted = networkOf();
step(`restricted: ${restricted}`);
if (!restricted.startsWith('ymir-agents |restricted'))
  throw new Error(`expected restricted network, got ${restricted}`);
const probe = (url) => {
  try {
    return podman(
      'exec',
      container,
      'node',
      '-e',
      `fetch(${JSON.stringify(url)},{signal:AbortSignal.timeout(4000)}).then(r=>console.log('OK',r.status)).catch(e=>console.log('BLOCKED',e.cause?.code||e.name))`,
    );
  } catch (e) {
    return `ERROR ${e.message}`;
  }
};
step(
  `restricted probes: litellm=${probe('http://litellm:5199/health/liveliness')} internet=${probe('http://1.1.1.1/')}`,
);
step(`assistant replied in restricted mode: ${restrictedReply.slice(0, 40)}`);
await worker.goto(`${baseUrl}/settings`);
await worker.locator('app-my-extensions-card li:has-text("對外連線")').waitFor();
await worker.screenshot({ path: `${outDir}/15-my-extensions-restricted.png`, fullPage: true });

await admin.goto(`${baseUrl}/admin/users`);
await admin.fill('app-users-page input[name=search]', account);
await admin.click('app-users-page .search button');
await row.locator('button:has-text("擴充能力")').click();
await admin.locator('app-users-page .extension-form select[name=internet]').selectOption('Inherit');
await admin.locator('app-users-page .extension-form button:has-text("儲存")').click();
await admin.locator('app-users-page [role=status]:has-text("擴充能力")').waitFor();
await worker.goto(`${baseUrl}/`);
await send(worker, '第三則（恢復對外連線）');
const back = networkOf();
step(`internet again: ${back}; probes: internet=${probe('http://1.1.1.1/')}`);
if (!back.startsWith('ymir-agents-net |internet'))
  throw new Error(`expected internet network, got ${back}`);

const audit = await admin.evaluate(
  async (userId) =>
    (
      await (
        await fetch(`/api/admin/audit?action=runtime.recreate&userId=${userId}`, {
          headers: {
            'X-XSRF-TOKEN': decodeURIComponent(
              document.cookie.match(/XSRF-TOKEN=([^;]+)/)?.[1] ?? '',
            ),
          },
        })
      ).json()
    ).items.length,
  me,
);
step(`runtime.recreate audit entries: ${audit}`);
await browser.close();
