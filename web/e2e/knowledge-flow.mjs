// 專案知識庫的端對端驗證（ADR-0014）：建立專案 → 上傳文件 → 索引完成 → 根據文件提問並看到引用。
// 前置：SQL Server、Fake LLM（dotnet run --project tests/Ymir.Testing.FakeLlm，含 /v1/embeddings）；
//       API 設定 VibeMaker__Pi__ModelBaseUrl=http://127.0.0.1:5199/v1、VibeMaker__Rag__EmbeddingModel=fake-embedding、
//       VibeMaker__Models__0__Id=fake-model、VibeMaker__Models__0__AllowKnowledgeBase=true；ng serve。
// 用法：node e2e/knowledge-flow.mjs <screenshot-dir> [baseUrl]
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';

const [outDir = '.', baseUrl = 'http://localhost:4200'] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
const browser = await chromium.launch(
  process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {},
);
const step = (name) => console.log(`✔ ${name}`);
const stamp = Date.now();

const page = await (
  await browser.newContext({ viewport: { width: 1280, height: 1000 } })
).newPage();
await page.goto(`${baseUrl}/login`);
await page.waitForSelector('details.dev');
if (!(await page.locator('details.dev').evaluate((d) => d.open)))
  await page.click('details.dev summary');
await page.fill('input[name=devAccount]', `kb-${stamp}`);
await page.click('details.dev button[type=submit]');
await page.waitForSelector('app-new-chat-page h1');

// 1. 建立專案
await page.click('button[aria-label="新增專案"]');
await page.fill('input[name=projectName]', '人事規章');
await page.press('input[name=projectName]', 'Enter');
await page.waitForSelector('app-project-page h1:has-text("人事規章")');
step('created a project');

// 2. 上傳文件並等待索引
const files = [
  ['請假規則.txt', '特別休假每年十四天，需要提前三天在系統申請。\n\n病假需附醫院證明。'],
  ['停車.md', '# 停車\n\n員工停車場在地下二樓，訪客請在櫃台登記車牌。'],
];
const card = page.locator('app-knowledge-card');
await card
  .locator('input[type=file]')
  .setInputFiles(
    files.map(([name, content]) => ({
      name,
      mimeType: 'text/plain',
      buffer: Buffer.from(content),
    })),
  );
await card.locator('li[data-status="Ready"]:has-text("請假規則.txt")').waitFor({ timeout: 30000 });
await card.locator('li[data-status="Ready"]:has-text("停車.md")').waitFor({ timeout: 30000 });
step('documents indexed');

// 3. 提問
await card.locator('textarea[name=knowledgeQuestion]').fill('特別休假要提前幾天申請？');
await card.locator('button:has-text("提問")').click();
await card.locator('.answer .text:has-text("[1]")').waitFor({ timeout: 30000 });
await card.locator('.citations summary:has-text("請假規則.txt")').first().click();
await card.screenshot({ path: `${outDir}/01-knowledge-answer.png` });
step('answered with citations');

// 4. 文件沒有涵蓋的問題
await card.locator('textarea[name=knowledgeQuestion]').fill('zzzz qqqq');
await card.locator('button:has-text("提問")').click();
await card.locator('.answer:has-text("沒有足夠的資料")').waitFor();
step('insufficient data is reported');

await browser.close();
