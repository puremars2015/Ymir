import {
  afterRenderEffect,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  input,
  ViewEncapsulation,
} from '@angular/core';
import { Marked, Renderer, Tokens } from 'marked';

const SAFE_LINK = /^(https?:|mailto:)/i;

const escapeHtml = (text: string): string =>
  text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');

/**
 * Agent 的回覆可能受 prompt injection 影響，視為不可信：
 * - 原始 HTML 一律 escape 成文字；
 * - 連結只允許 http(s) / mailto，並在新分頁開啟；
 * - 圖片不載入，只顯示成連結，避免以圖片網址把資料帶到外部（追蹤像素）。
 * 輸出再經過 Angular 的 [innerHTML] sanitizer（不使用 bypassSecurityTrust）。
 */
const renderer = new Renderer();
renderer.html = ({ text }: Tokens.HTML | Tokens.Tag) => escapeHtml(text);
renderer.link = function ({ href, title, tokens }: Tokens.Link) {
  const text = this.parser.parseInline(tokens);
  if (!SAFE_LINK.test(href.trim())) {
    return text;
  }
  const titleAttr = title ? ` title="${escapeHtml(title)}"` : '';
  return `<a href="${escapeHtml(href)}"${titleAttr} target="_blank" rel="noopener noreferrer">${text}</a>`;
};
renderer.image = ({ href, text }: Tokens.Image) => {
  const label = escapeHtml(text || href);
  return SAFE_LINK.test(href.trim())
    ? `<a href="${escapeHtml(href)}" target="_blank" rel="noopener noreferrer">🖼 ${label}</a>`
    : label;
};

// 聊天習慣：單一換行就換行（breaks）；GFM 支援表格、刪除線、自動連結。
const markdown = new Marked({ gfm: true, breaks: true, async: false, renderer });

/** 把 Markdown 轉成 HTML 字串（純函式，供元件與測試使用）。 */
export function renderMarkdown(text: string): string {
  return markdown.parse(text, { async: false });
}

const COPY_LABEL = '複製';
const COPIED_LABEL = '已複製';

/**
 * 顯示 Agent 回覆的 Markdown。innerHTML 產生的元素沒有 Angular 的樣式屬性，
 * 所以用 ViewEncapsulation.None，樣式全部限定在 `app-markdown` 底下。
 */
@Component({
  selector: 'app-markdown',
  template: `<div class="markdown" [innerHTML]="html()"></div>`,
  styles: `
    app-markdown {
      display: block;
      min-width: 0;
    }
    app-markdown .markdown {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
      line-height: 1.7;
      overflow-wrap: anywhere;
    }
    app-markdown .markdown > * {
      margin: 0;
    }
    app-markdown h1,
    app-markdown h2,
    app-markdown h3,
    app-markdown h4,
    app-markdown h5,
    app-markdown h6 {
      line-height: 1.4;
      font-weight: 650;
      margin: 0.5rem 0 0;
    }
    app-markdown h1 {
      font-size: 1.375rem;
    }
    app-markdown h2 {
      font-size: 1.1875rem;
    }
    app-markdown h3 {
      font-size: 1.0625rem;
    }
    app-markdown h4,
    app-markdown h5,
    app-markdown h6 {
      font-size: 1rem;
    }
    app-markdown ul,
    app-markdown ol {
      padding-left: 1.5rem;
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    app-markdown ul {
      list-style: disc;
    }
    app-markdown ol {
      list-style: decimal;
    }
    app-markdown li > p {
      margin: 0;
    }
    app-markdown li > ul,
    app-markdown li > ol {
      margin-top: 0.25rem;
    }
    app-markdown a {
      color: var(--accent);
      text-decoration: underline;
      text-underline-offset: 2px;
    }
    app-markdown code {
      font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
      font-size: 0.875em;
    }
    app-markdown :not(pre) > code {
      background: var(--surface-muted);
      border: 1px solid var(--border);
      border-radius: 0.375rem;
      padding: 0.0625rem 0.375rem;
    }
    app-markdown pre {
      position: relative;
      margin: 0;
      background: var(--surface-muted);
      border: 1px solid var(--border);
      border-radius: 0.75rem;
      padding: 2.25rem 1rem 0.875rem;
      overflow-x: auto;
      line-height: 1.55;
    }
    app-markdown pre code {
      white-space: pre;
      overflow-wrap: normal;
    }
    app-markdown .code-lang {
      position: absolute;
      top: 0.5rem;
      left: 1rem;
      font-size: 0.75rem;
      color: var(--text-muted);
      text-transform: lowercase;
    }
    app-markdown .code-copy {
      position: absolute;
      top: 0.375rem;
      right: 0.5rem;
      font-size: 0.75rem;
      padding: 0.125rem 0.5rem;
      border-radius: 0.375rem;
      background: transparent;
      color: var(--text-muted);
      border: 1px solid var(--border);
    }
    app-markdown .code-copy:hover {
      color: var(--text);
      border-color: var(--text-muted);
    }
    app-markdown blockquote {
      border-left: 3px solid var(--border);
      padding-left: 0.875rem;
      color: var(--text-muted);
    }
    app-markdown hr {
      border: none;
      border-top: 1px solid var(--border);
      width: 100%;
    }
    app-markdown .table-wrap {
      overflow-x: auto;
    }
    app-markdown table {
      border-collapse: collapse;
      font-size: 0.9375rem;
    }
    app-markdown th,
    app-markdown td {
      border: 1px solid var(--border);
      padding: 0.375rem 0.75rem;
      text-align: left;
      vertical-align: top;
    }
    app-markdown th {
      background: var(--surface-muted);
      font-weight: 600;
    }
  `,
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Markdown {
  readonly text = input.required<string>();
  protected readonly html = computed(() => renderMarkdown(this.text()));

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly timers = new Set<ReturnType<typeof setTimeout>>();

  constructor() {
    inject(DestroyRef).onDestroy(() => this.timers.forEach((t) => clearTimeout(t)));
    // 複製按鈕與語言標籤以 DOM API 加上，不放進 HTML 字串（sanitizer 會移除 button）。
    // 串流中內容會重繪，已經加過的 pre 跳過。
    afterRenderEffect(() => {
      this.html();
      this.decorate();
    });
  }

  private decorate(): void {
    const root = this.host.nativeElement;
    root.querySelectorAll('table').forEach((table) => {
      if (!table.parentElement?.classList.contains('table-wrap')) {
        const wrap = document.createElement('div');
        wrap.className = 'table-wrap';
        table.replaceWith(wrap);
        wrap.appendChild(table);
      }
    });
    root.querySelectorAll('pre').forEach((pre) => {
      if (pre.querySelector(':scope > .code-copy')) {
        return;
      }
      const code = pre.querySelector('code');
      const lang = [...(code?.classList ?? [])]
        .find((c) => c.startsWith('language-'))
        ?.slice('language-'.length);
      if (lang) {
        const label = document.createElement('span');
        label.className = 'code-lang';
        label.textContent = lang;
        pre.appendChild(label);
      }
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'code-copy';
      button.textContent = COPY_LABEL;
      button.setAttribute('aria-label', '複製程式碼');
      button.addEventListener('click', () => this.copy(button, code?.textContent ?? ''));
      pre.appendChild(button);
    });
  }

  private copy(button: HTMLButtonElement, text: string): void {
    void navigator.clipboard
      ?.writeText(text)
      .then(() => {
        button.textContent = COPIED_LABEL;
        const timer = setTimeout(() => {
          button.textContent = COPY_LABEL;
          this.timers.delete(timer);
        }, 1500);
        this.timers.add(timer);
      })
      .catch(() => {
        button.textContent = '無法複製';
      });
  }
}
