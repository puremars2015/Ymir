// 附件的端對端驗證（手動執行，尚未進 CI）：首頁直接附加圖片開聊、對話中附加多個檔案、重新整理後仍顯示。
// 前置：SQL Server、Fake LLM、API（VibeMaker__Harness=Pi；要驗證圖片送進模型時，
// 加上 VibeMaker__Models__0__Id=<Pi ModelId> 與 VibeMaker__Models__0__SupportsImages=true）、ng serve 都已啟動。
// 用法：node e2e/attachment-flow.mjs <screenshot-dir> [baseUrl]
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const executablePath = process.env.CHROMIUM_PATH;
const browser = await chromium.launch(executablePath ? { executablePath } : {});
const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
const step = (name) => console.log(`✔ ${name}`);
const composer = 'app-composer textarea';
const fileInput = 'app-composer input[type=file]';
const waitIdle = () => page.waitForSelector('.turn.live', { state: 'detached', timeout: 60000 });
// 1x1 PNG
const png = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==',
  'base64',
);

// 1. Dev 登入
await page.goto(baseUrl);
await page.waitForURL(/\/login/);
await page.waitForSelector('details.dev');
if (!(await page.locator('details.dev').evaluate((d) => d.open))) {
  await page.click('details.dev summary');
}
await page.fill('input[name=devAccount]', `e2e-attach-${Date.now()}`);
await page.click('details.dev button[type=submit]');
await page.waitForSelector('app-new-chat-page h1');
step('login');

// 2. 首頁附加圖片 → 顯示 chip → 送出（對話建立後才上傳）
await page.setInputFiles(fileInput, { name: '截圖.png', mimeType: 'image/png', buffer: png });
await page.waitForSelector('app-composer .attachment:has-text("截圖.png")');
await page.screenshot({ path: `${outDir}/attach-01-pending.png` });
await page.fill(composer, '請描述這張圖');
await page.press(composer, 'Enter');
await page.waitForURL(/\/c\//);
await page.waitForSelector('.turn.user app-attachment-list img[alt="截圖.png"]', {
  timeout: 30000,
});
await waitIdle();
const reply = await page.locator('.turn.assistant:not(.turn-files)').last().innerText();
if (!reply.includes('uploads/'))
  throw new Error(`agent did not receive the attachment path: ${reply}`);
const sawImage = reply.includes('（收到圖片）1');
if ((await page.locator('app-artifact-download a').count()) > 0) {
  throw new Error('uploaded attachments must not be listed as files produced by the agent');
}
await page.screenshot({ path: `${outDir}/attach-02-sent.png` });
step(
  `new chat with image attachment (model ${sawImage ? 'received the image' : 'got the file path only'})`,
);

// 3. 對話中只附檔案、不輸入文字（拖放 / 選檔都走同一條路）：一張圖 + 一個影片
await page.setInputFiles(fileInput, [
  { name: 'second.png', mimeType: 'image/png', buffer: png },
  {
    name: 'demo.mp4',
    mimeType: 'video/mp4',
    buffer: Buffer.from([0, 0, 0, 24, 0x66, 0x74, 0x79, 0x70]),
  },
]);
await page.waitForSelector('app-composer .attachment:has-text("demo.mp4")');
await page.click('app-composer button[type=submit]');
await page.waitForSelector('.turn.user app-attachment-list .chip:has-text("demo.mp4")', {
  timeout: 30000,
});
await waitIdle();
step('attachments without text (image + video)');

// 4. 重新整理後附件仍顯示，檔案面板列出 uploads/ 的檔案
await page.reload();
await page.waitForSelector('.turn.user app-attachment-list img[alt="second.png"]', {
  timeout: 30000,
});
const thumbs = await page.locator('.turn.user app-attachment-list img').count();
if (thumbs !== 2) throw new Error(`expected 2 thumbnails after reload, got ${thumbs}`);
await page.click('.files-toggle');
await page.getByRole('button', { name: '專案檔案', exact: true }).click();
await page.waitForSelector('app-files-panel .file:has-text("demo.mp4")');
const listed = await page.locator('app-files-panel .file').count();
if (listed !== 3) throw new Error(`expected 3 uploaded files in the panel, got ${listed}`);
await page.screenshot({ path: `${outDir}/attach-03-reload.png` });
step('attachments persist after reload and appear in the files panel');

await browser.close();
