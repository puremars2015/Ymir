import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { toolFrameUrl } from '../../core/tools/tool-rules';
import { ToolStore } from '../../core/tools/tool.store';

/**
 * 開啟一個小工具（ADR-0029）：以 Ymir 的登入身分取得一次性票據，在 iframe 內載入網站網域上的頁面。
 * 內容在網站網域（不同來源）執行，拿不到 Ymir 的 cookie 或 API；Ymir 的 CSP 只允許框住網站網域。
 */
@Component({
  selector: 'app-tool-page',
  imports: [RouterLink],
  template: `
    <section class="tool">
      <header class="bar">
        <h1 [title]="name()">{{ name() }}</h1>
        @if (tool(); as t) {
          <a class="link" [routerLink]="['/c', t.conversationId]">回到對話修改</a>
        }
        <button type="button" class="secondary" [disabled]="!frame()" (click)="openInNewTab()">
          開新分頁
        </button>
      </header>
      @if (error(); as e) {
        <p class="error" role="alert">{{ e }}</p>
      } @else if (frame(); as src) {
        <iframe
          [src]="src"
          [title]="name()"
          sandbox="allow-scripts allow-same-origin allow-forms allow-popups allow-modals allow-downloads"
          referrerpolicy="no-referrer"
        ></iframe>
      } @else {
        <p class="muted" role="status">正在開啟小工具…</p>
      }
    </section>
  `,
  styles: `
    :host {
      flex: 1;
      display: flex;
      min-height: 0;
    }
    .tool {
      flex: 1;
      display: flex;
      flex-direction: column;
      min-height: 0;
      padding: 0.75rem 1rem 1rem;
      gap: 0.75rem;
    }
    .bar {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      flex-wrap: wrap;
    }
    h1 {
      margin: 0;
      font-size: 1.125rem;
      flex: 1;
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    iframe {
      flex: 1;
      width: 100%;
      min-height: 20rem;
      border: 1px solid var(--border);
      border-radius: 0.5rem;
      background: #fff;
    }
    p {
      margin: 0;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ToolPage {
  private readonly api = inject(ApiService);
  private readonly tools = inject(ToolStore);
  private readonly sanitizer = inject(DomSanitizer);

  /** 路由參數（網站 id）。 */
  readonly siteId = input.required<string>();

  protected readonly tool = computed(() => this.tools.tools().find((t) => t.id === this.siteId()));
  protected readonly name = computed(() => this.tool()?.name ?? '小工具');
  protected readonly frame = signal<SafeResourceUrl | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      const id = this.siteId();
      untracked(() => this.open(id));
    });
  }

  protected openInNewTab(): void {
    // 票據只能用一次：另外取一張給新分頁。
    this.issue(this.siteId(), (url) => window.open(url, '_blank', 'noopener'));
  }

  private open(id: string): void {
    this.frame.set(null);
    this.error.set(null);
    if (!this.tools.loaded()) this.tools.refresh();
    this.issue(id, (url) => {
      if (id !== this.siteId()) return;
      // 已檢查是網站網域的 http(s) 網址（toolFrameUrl），才交給 iframe。
      this.frame.set(this.sanitizer.bypassSecurityTrustResourceUrl(url));
    });
  }

  private issue(id: string, done: (url: string) => void): void {
    this.api.issueSiteTicket(id, '/').subscribe({
      next: (ticket) => {
        const url = toolFrameUrl(ticket.redirectUrl, window.location.origin);
        if (url) done(url);
        else this.error.set('無法開啟這個小工具。');
      },
      error: (e: unknown) =>
        this.error.set(
          e instanceof HttpErrorResponse && (e.status === 404 || e.status === 403)
            ? '找不到這個小工具，可能已被刪除或取消發布。'
            : describeApiError(e),
        ),
    });
  }
}
