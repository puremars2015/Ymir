import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { SharedSite, Site, Sites } from '../../core/api/api-types';
import { formatSize } from '../../core/files/workspace-files';
import { siteAccessLabel } from '../../core/sites/site-rules';
import { SiteAccessEditor } from './site-access-editor';

/**
 * 我的網站（ADR-0016）：已發布的網站、網址、重新發布、取消發布與刪除。
 * 網站在獨立網域上開啟（新分頁），不在 Ymir 的網域內顯示。
 */
@Component({
  selector: 'app-sites-page',
  imports: [DatePipe, RouterLink, SiteAccessEditor],
  template: `
    <section class="page stack">
      <h1>我的網站</h1>
      @if (data(); as d) {
        @if (!d.enabled) {
          <p class="muted">網站託管尚未設定，請洽管理員。</p>
        } @else {
          <p class="muted small">
            在對話的「檔案」面板選擇含有 index.html 的目錄即可發布（最多 {{ d.maxSites }} 個網站）。
            修改檔案後需要重新發布才會更新。
          </p>
        }
        <ul class="sites">
          @for (site of d.sites; track site.id) {
            <li class="card stack" [attr.data-site]="site.name">
              <div class="row between">
                <strong>{{ site.name }}</strong>
                <span class="muted small"
                  >{{ site.status === 'Published' ? '已發布' : '未發布' }} ·
                  {{ access(site) }}</span
                >
              </div>
              @if (site.url && site.status === 'Published') {
                <a class="url" [href]="site.url" target="_blank" rel="noopener noreferrer">{{
                  site.url
                }}</a>
              }
              <p class="muted small">
                來源：{{ site.sourcePath }} · {{ site.fileCount }} 個檔案 · {{ size(site) }}
                @if (site.publishedAt) {
                  · {{ site.publishedAt | date: 'M/d HH:mm' }} 發布
                }
                · <a [routerLink]="['/c', site.conversationId]">來源對話</a>
              </p>
              <div class="row">
                <button type="button" [disabled]="busy()" (click)="republish(site)">
                  重新發布
                </button>
                @if (site.status === 'Published') {
                  <button
                    type="button"
                    class="secondary"
                    [disabled]="busy()"
                    (click)="unpublish(site)"
                  >
                    取消發布
                  </button>
                }
                <button
                  type="button"
                  class="secondary"
                  [attr.aria-expanded]="editing() === site.id"
                  (click)="toggleAccess(site)"
                >
                  存取設定
                </button>
                <button
                  type="button"
                  class="link danger"
                  [disabled]="busy()"
                  (click)="remove(site)"
                >
                  刪除
                </button>
              </div>
              @if (editing() === site.id) {
                <app-site-access-editor [site]="site" (saved)="accessSaved($event)" />
              }
            </li>
          } @empty {
            <li class="muted">還沒有網站。</li>
          }
        </ul>
      } @else if (!error()) {
        <p class="muted">載入中…</p>
      }
      @if (shared().length > 0) {
        <h2>分享給我的網站</h2>
        <ul class="sites" aria-label="分享給我的網站">
          @for (site of shared(); track site.id) {
            <li class="card row between" [attr.data-shared-site]="site.name">
              <span
                ><strong>{{ site.name }}</strong>
                <span class="muted small">· {{ site.ownerName }}</span></span
              >
              @if (site.url) {
                <a [href]="site.url" target="_blank" rel="noopener noreferrer">開啟</a>
              }
            </li>
          }
        </ul>
      }
      @if (message(); as m) {
        <p class="muted" role="status">{{ m }}</p>
      }
      @if (error(); as e) {
        <p class="error" role="alert">{{ e }}</p>
      }
    </section>
  `,
  styles: `
    .page {
      max-width: 52rem;
      margin: 0 auto;
      padding: 1.5rem 1rem;
    }
    h1 {
      margin: 0;
      font-size: 1.5rem;
    }
    h2 {
      margin: 0.5rem 0 0;
      font-size: 1.125rem;
    }
    p {
      margin: 0;
    }
    .small {
      font-size: 0.8125rem;
    }
    .sites {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }
    .row {
      display: flex;
      gap: 0.75rem;
      align-items: center;
      flex-wrap: wrap;
    }
    .between {
      justify-content: space-between;
    }
    .url {
      overflow-wrap: anywhere;
    }
    .danger {
      color: var(--danger);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SitesPage implements OnInit {
  private readonly api = inject(ApiService);

  protected readonly data = signal<Sites | null>(null);
  protected readonly shared = signal<SharedSite[]>([]);
  protected readonly editing = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  protected access(site: Site): string {
    return siteAccessLabel(site.accessMode);
  }

  protected toggleAccess(site: Site): void {
    this.editing.update((id) => (id === site.id ? null : site.id));
  }

  protected accessSaved(site: Site): void {
    this.editing.set(null);
    this.message.set(`已更新「${site.name}」的存取設定：${siteAccessLabel(site.accessMode)}。`);
    this.load();
  }

  protected size(site: Site): string {
    return formatSize(site.totalBytes);
  }

  protected republish(site: Site): void {
    this.run(this.api.republishSite(site.id), `已重新發布「${site.name}」。`);
  }

  protected unpublish(site: Site): void {
    if (confirm(`取消發布「${site.name}」？網址會立即停止提供內容，檔案與網址保留，可以再發布。`)) {
      this.run(this.api.unpublishSite(site.id), `已取消發布「${site.name}」。`);
    }
  }

  protected remove(site: Site): void {
    if (confirm(`刪除「${site.name}」？網址與發布的檔案會一起刪除（對話中的原始檔案保留）。`)) {
      this.run(this.api.deleteSite(site.id), `已刪除「${site.name}」。`);
    }
  }

  private run(request: Observable<unknown>, done: string): void {
    this.busy.set(true);
    this.message.set(null);
    this.error.set(null);
    request.subscribe({
      next: () => {
        this.message.set(done);
        this.busy.set(false);
        this.load();
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.busy.set(false);
      },
    });
  }

  private load(): void {
    this.api.listSites().subscribe({
      next: (data) => this.data.set(data),
      error: (e: unknown) => this.error.set(describeApiError(e)),
    });
    this.api.sitesSharedWithMe().subscribe({
      next: (sites) => this.shared.set(sites),
      error: (e: unknown) => this.error.set(describeApiError(e)),
    });
  }
}
