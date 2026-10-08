import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { Site, WorkspaceFile } from '../../core/api/api-types';
import { publishableDirectories } from '../../core/sites/site-rules';

/**
 * 檔案面板的「發布網站」（ADR-0016）：選一個含有 index.html 的目錄發布成網站；網址在獨立網域上（新分頁開啟）。
 */
@Component({
  selector: 'app-publish-site',
  imports: [FormsModule, RouterLink],
  template: `
    @if (directories().length > 0) {
      <details class="publish" [open]="open()" (toggle)="open.set($any($event.target).open)">
        <summary>🌐 發布網站</summary>
        <form class="stack" (ngSubmit)="publish()">
          <label
            >網站名稱
            <input
              name="siteName"
              [ngModel]="name()"
              (ngModelChange)="name.set($event)"
              maxlength="100"
            />
          </label>
          <label
            >來源目錄
            <select
              name="siteSource"
              [ngModel]="source()"
              (ngModelChange)="sourceChoice.set($event)"
            >
              @for (dir of directories(); track dir) {
                <option [value]="dir">{{ dir === '.' ? '（工作目錄）' : dir }}</option>
              }
            </select>
          </label>
          <label class="check">
            <input
              type="checkbox"
              name="siteSpa"
              [ngModel]="spa()"
              (ngModelChange)="spa.set($event)"
            />
            <span>單頁應用（React / Vue / Angular 路由）</span>
          </label>
          <button type="submit" [disabled]="busy()">{{ busy() ? '發布中…' : '發布' }}</button>
        </form>
        @if (site(); as s) {
          <p class="small" role="status">
            已發布：
            <a [href]="s.url" target="_blank" rel="noopener noreferrer">{{ s.url }}</a>
            · <a routerLink="/sites">管理我的網站</a>
          </p>
        }
        @if (error(); as e) {
          <p class="error small" role="alert">{{ e }}</p>
        }
      </details>
    }
  `,
  styles: `
    .publish {
      border: 1px solid var(--border);
      border-radius: 0.5rem;
      padding: 0.5rem 0.75rem;
      font-size: 0.875rem;
    }
    summary {
      cursor: pointer;
      font-weight: 600;
    }
    form {
      margin-top: 0.5rem;
    }
    label {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    .check {
      flex-direction: row;
      align-items: center;
      gap: 0.5rem;
    }
    p {
      margin: 0.5rem 0 0;
      overflow-wrap: anywhere;
    }
    .small {
      font-size: 0.8125rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PublishSite {
  private readonly api = inject(ApiService);

  readonly conversationId = input.required<string>();
  readonly files = input.required<WorkspaceFile[]>();

  protected readonly directories = computed(() => publishableDirectories(this.files()));
  protected readonly open = signal(false);
  protected readonly name = signal('我的網站');
  protected readonly sourceChoice = signal<string | null>(null);
  protected readonly source = computed(() => this.sourceChoice() ?? this.directories()[0] ?? '.');
  protected readonly spa = signal(false);
  protected readonly busy = signal(false);
  protected readonly site = signal<Site | null>(null);
  protected readonly error = signal<string | null>(null);

  protected publish(): void {
    this.busy.set(true);
    this.error.set(null);
    this.site.set(null);
    this.api
      .publishSite(this.conversationId(), {
        name: this.name().trim() || '我的網站',
        sourcePath: this.source(),
        spaMode: this.spa(),
      })
      .subscribe({
        next: (site) => {
          this.site.set(site);
          this.busy.set(false);
        },
        error: (e: unknown) => {
          this.error.set(describeApiError(e));
          this.busy.set(false);
        },
      });
  }
}
