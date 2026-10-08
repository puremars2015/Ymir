import {
  ChangeDetectionStrategy,
  Component,
  inject,
  input,
  OnInit,
  output,
  signal,
} from '@angular/core';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { Site, SiteAccessMode, UserSearchResult } from '../../core/api/api-types';
import {
  addShare,
  removeShare,
  ShareTarget,
  siteAccessHint,
  siteAccessLabel,
  siteAccessModes,
} from '../../core/sites/site-rules';

/**
 * 網站的存取設定（ADR-0016 §3）：公開 / 所有 Ymir 使用者 / 指定使用者。
 * 擁有者與 Ymir 管理員一律可以看；分享只授予瀏覽權，不包含原始碼。
 */
@Component({
  selector: 'app-site-access-editor',
  template: `
    <fieldset class="stack" [attr.data-access-editor]="site().name">
      <legend>誰可以看</legend>
      <div class="row">
        @for (mode of modes; track mode) {
          <label class="choice">
            <input
              type="radio"
              [name]="'access-' + site().id"
              [value]="mode"
              [checked]="selected() === mode"
              (change)="selected.set(mode)"
            />
            {{ label(mode) }}
          </label>
        }
      </div>
      <p class="muted small">{{ hint(selected()) }} 你與 Ymir 管理員一律可以看。</p>

      @if (selected() === 'SelectedUsers') {
        <ul class="chips" aria-label="分享名單">
          @for (user of shares(); track user.userId) {
            <li class="chip">
              {{ user.displayName }}
              @if (user.accountName) {
                <span class="muted small">{{ user.accountName }}</span>
              }
              <button
                type="button"
                class="link"
                [attr.aria-label]="'移除 ' + user.displayName"
                (click)="remove(user.userId)"
              >
                ×
              </button>
            </li>
          } @empty {
            <li class="muted small">還沒有分享給任何人。</li>
          }
        </ul>
        <form class="row" (submit)="$event.preventDefault(); search()">
          <input
            type="search"
            name="user-search"
            placeholder="搜尋名稱或帳號"
            aria-label="搜尋使用者"
            [value]="query()"
            (input)="query.set($any($event.target).value)"
          />
          <button type="submit" class="secondary" [disabled]="!query().trim()">搜尋</button>
        </form>
        @if (results(); as list) {
          <ul class="results">
            @for (user of list; track user.id) {
              <li class="row between">
                <span
                  >{{ user.displayName }}
                  @if (user.accountName) {
                    <span class="muted small">{{ user.accountName }}</span>
                  }
                </span>
                <button type="button" class="link" (click)="add(user)">加入</button>
              </li>
            } @empty {
              <li class="muted small">找不到符合的使用者。</li>
            }
          </ul>
        }
      }

      <div class="row">
        <button type="button" [disabled]="busy()" (click)="save()">儲存存取設定</button>
        @if (error(); as e) {
          <span class="error" role="alert">{{ e }}</span>
        }
      </div>
    </fieldset>
  `,
  styles: `
    fieldset {
      border: 1px solid var(--border);
      border-radius: 0.5rem;
      padding: 0.75rem;
      margin: 0;
    }
    legend {
      font-size: 0.875rem;
      padding: 0 0.25rem;
    }
    p {
      margin: 0;
    }
    .small {
      font-size: 0.8125rem;
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
    .choice {
      display: flex;
      flex-direction: row;
      gap: 0.25rem;
      align-items: center;
    }
    .chips,
    .results {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      gap: 0.5rem;
      flex-wrap: wrap;
    }
    .results {
      flex-direction: column;
    }
    .chip {
      display: flex;
      gap: 0.25rem;
      align-items: center;
      padding: 0.125rem 0.5rem;
      border: 1px solid var(--border);
      border-radius: 999px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SiteAccessEditor implements OnInit {
  private readonly api = inject(ApiService);

  readonly site = input.required<Site>();
  readonly saved = output<Site>();

  protected readonly modes = siteAccessModes;
  protected readonly selected = signal<SiteAccessMode>('Public');
  protected readonly shares = signal<ShareTarget[]>([]);
  protected readonly query = signal('');
  protected readonly results = signal<UserSearchResult[] | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.selected.set(this.site().accessMode);
    this.shares.set(this.site().sharedWith.map((s) => ({ ...s })));
  }

  protected label(mode: SiteAccessMode): string {
    return siteAccessLabel(mode);
  }

  protected hint(mode: SiteAccessMode): string {
    return siteAccessHint(mode);
  }

  protected search(): void {
    const q = this.query().trim();
    if (!q) {
      return;
    }
    this.api.searchUsers(q).subscribe({
      next: (users) => this.results.set(users),
      error: (e: unknown) => this.error.set(describeApiError(e)),
    });
  }

  protected add(user: UserSearchResult): void {
    this.shares.update((list) =>
      addShare(list, {
        userId: user.id,
        displayName: user.displayName,
        accountName: user.accountName,
      }),
    );
  }

  protected remove(userId: string): void {
    this.shares.update((list) => removeShare(list, userId));
  }

  protected save(): void {
    this.busy.set(true);
    this.error.set(null);
    const mode = this.selected();
    const userIds = mode === 'SelectedUsers' ? this.shares().map((s) => s.userId) : [];
    this.api.setSiteAccess(this.site().id, mode, userIds).subscribe({
      next: (site) => {
        this.busy.set(false);
        this.saved.emit(site);
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.busy.set(false);
      },
    });
  }
}
