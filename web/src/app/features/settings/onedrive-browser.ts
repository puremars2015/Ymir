import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { OneDriveBrowserItem } from '../../core/api/api-types';

/** ADR-0022：只列出使用者自己的 drive；下載與寫回仍須由明確連結決定。 */
@Component({
  selector: 'app-onedrive-browser',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="stack" aria-label="瀏覽自己的 OneDrive">
      <h3>瀏覽自己的 OneDrive</h3>
      <p class="muted hint">
        可瀏覽同步資料夾以外的檔案；瀏覽不會將檔案交給 Agent 或修改雲端資料。
      </p>
      <form class="row" (ngSubmit)="search()">
        <label
          >搜尋檔案或資料夾
          <input
            name="onedriveQuery"
            maxlength="200"
            [ngModel]="query()"
            (ngModelChange)="query.set($event)"
          />
        </label>
        <button type="submit" [disabled]="busy() || !query().trim()">搜尋</button>
        <button type="button" class="secondary" [disabled]="busy()" (click)="browse()">
          根目錄
        </button>
        @if (parentId(); as parent) {
          <button type="button" class="secondary" [disabled]="busy()" (click)="browse(parent)">
            上一層
          </button>
        }
      </form>
      @if (busy()) {
        <p role="status">正在讀取 OneDrive…</p>
      }
      @if (error(); as error) {
        <p class="error" role="alert">{{ error }}</p>
      }
      <ul aria-label="OneDrive 項目">
        @for (item of items(); track item.id) {
          <li>
            @if (item.isFolder) {
              <button type="button" class="link" [disabled]="busy()" (click)="browse(item.id)">
                📁 {{ item.name }}
              </button>
            } @else {
              <span>📄 {{ item.name }}</span>
            }
          </li>
        } @empty {
          @if (!busy() && !error()) {
            <li class="muted">沒有可顯示的項目。</li>
          }
        }
      </ul>
      @if (searching()) {
        <p class="muted hint">搜尋最多顯示 50 筆，不包含與我共用或 SharePoint 項目。</p>
      }
    </section>
  `,
  styles: `
    h3,
    p {
      margin: 0;
    }
    h3 {
      font-size: 1rem;
    }
    .hint {
      font-size: 0.8125rem;
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: end;
      gap: 0.5rem;
    }
    label {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    ul {
      list-style: none;
      padding: 0;
      margin: 0;
      max-height: 20rem;
      overflow: auto;
    }
    li {
      padding: 0.375rem;
      overflow-wrap: anywhere;
    }
  `,
})
export class OneDriveBrowser {
  private readonly api = inject(ApiService);
  protected readonly query = signal('');
  protected readonly items = signal<OneDriveBrowserItem[]>([]);
  protected readonly parentId = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly searching = signal(false);
  protected readonly error = signal<string | null>(null);
  private request: Subscription | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
    this.browse();
  }

  protected browse(itemId?: string): void {
    this.prepare(false);
    this.request = this.api.browseOneDrive(itemId).subscribe({
      next: (page) => {
        this.items.set(page.items);
        this.parentId.set(page.parentItemId);
        this.busy.set(false);
      },
      error: (error: unknown) => this.failed(error),
    });
  }

  protected search(): void {
    const query = this.query().trim();
    if (!query) return;
    this.prepare(true);
    this.request = this.api.searchOneDrive(query).subscribe({
      next: (items) => {
        this.items.set(items);
        this.busy.set(false);
      },
      error: (error: unknown) => this.failed(error),
    });
  }

  private prepare(searching: boolean): void {
    this.request?.unsubscribe();
    this.items.set([]);
    this.parentId.set(null);
    this.busy.set(true);
    this.searching.set(searching);
    this.error.set(null);
  }

  private failed(error: unknown): void {
    this.busy.set(false);
    this.error.set(describeApiError(error));
  }
}
