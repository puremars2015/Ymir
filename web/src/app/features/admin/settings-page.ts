import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import {
  localToday,
  OidcDraft,
  oidcDraftFrom,
  oidcDraftProblem,
  oidcSourceLabel,
  secretExpiry,
  toSaveOidcRequest,
} from '../../core/admin/oidc-settings-rules';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { OidcSettings, OidcTestResult } from '../../core/api/api-types';
import { AdminTabs } from './admin-tabs';

/**
 * 系統設定（ADR-0010）：企業帳號（Entra ID）登入。
 * client secret 只能寫入，畫面上只顯示是否已設定與更新時間；存檔後不必重啟。
 */
@Component({
  selector: 'app-settings-page',
  imports: [AdminTabs, DatePipe, FormsModule],
  template: `
    <section class="admin">
      <app-admin-tabs />
      <h1>系統設定</h1>

      <article class="card stack oidc">
        <header class="section-head">
          <div>
            <h2>企業帳號登入（Microsoft Entra ID）</h2>
            @if (settings(); as s) {
              <p class="muted small">
                目前：
                <span class="state" [class.on]="s.configured">{{
                  s.configured ? '已啟用' : '未啟用'
                }}</span>
                · 設定來源：{{ sourceLabel() }}
                @if (s.updatedAt) {
                  · {{ s.updatedAt | date: 'yyyy-MM-dd HH:mm' }}
                  {{ s.updatedByName ? '由 ' + s.updatedByName + ' 更新' : '' }}
                }
              </p>
            }
          </div>
        </header>

        @if (settings(); as s) {
          <div class="redirect">
            <span class="muted small">Entra「驗證」頁要加入的重新導向 URI（平台：Web）</span>
            <code>{{ s.redirectUri }}</code>
          </div>

          <form class="stack" (ngSubmit)="save()">
            <label class="check">
              <input
                type="checkbox"
                name="enabled"
                [ngModel]="draft().enabled"
                (ngModelChange)="patch({ enabled: $event })"
              />
              啟用企業帳號登入
            </label>
            <div class="grid">
              <label
                >Tenant ID（目錄識別碼）
                <input
                  name="tenantId"
                  autocomplete="off"
                  spellcheck="false"
                  placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                  [ngModel]="draft().tenantId"
                  (ngModelChange)="patch({ tenantId: $event }); testResult.set(null)"
              /></label>
              <label
                >Client ID（應用程式識別碼）
                <input
                  name="clientId"
                  autocomplete="off"
                  spellcheck="false"
                  [ngModel]="draft().clientId"
                  (ngModelChange)="patch({ clientId: $event })"
              /></label>
              <label
                >Client secret
                <input
                  name="clientSecret"
                  type="password"
                  autocomplete="new-password"
                  [placeholder]="secretPlaceholder()"
                  [ngModel]="draft().clientSecret"
                  (ngModelChange)="patch({ clientSecret: $event })"
                />
                <span class="muted small hint"
                  >填 Entra「憑證及祕密」的「值」，不是「祕密識別碼」。留空表示不變更。</span
                >
              </label>
              <label
                >Secret 到期日（選填，到期前 30 天提醒）
                <input
                  name="secretExpiresOn"
                  type="date"
                  [ngModel]="draft().secretExpiresOn"
                  (ngModelChange)="patch({ secretExpiresOn: $event })"
                />
                @if (expiry(); as e) {
                  @if (e.state === 'expired') {
                    <span class="small danger-text">已過期 {{ e.days }} 天</span>
                  } @else if (e.state === 'soon') {
                    <span class="small warn-text">{{ e.days }} 天後到期</span>
                  }
                }
              </label>
              <label
                >Admin app role
                <input
                  name="adminRole"
                  spellcheck="false"
                  [ngModel]="draft().adminRole"
                  (ngModelChange)="patch({ adminRole: $event })"
                />
                <span class="muted small hint"
                  >token 的 roles 含此值的人是管理員，每次登入同步。</span
                >
              </label>
              <label
                >登入按鈕名稱
                <input
                  name="displayName"
                  maxlength="30"
                  [ngModel]="draft().displayName"
                  (ngModelChange)="patch({ displayName: $event })"
                />
                <span class="muted small hint"
                  >登入頁顯示「以{{ draft().displayName }}登入」。</span
                >
              </label>
            </div>

            <div class="row">
              <button type="submit" [disabled]="busy() || !!problem()">儲存</button>
              <button
                type="button"
                class="secondary"
                [disabled]="busy() || !tenantLooksValid()"
                (click)="test()"
              >
                測試設定
              </button>
              @if (s.source === 'Database') {
                <button type="button" class="link" [disabled]="busy()" (click)="reset()">
                  還原為部署設定
                </button>
              }
              @if (problem(); as p) {
                <span class="muted small">{{ p }}</span>
              }
            </div>
          </form>

          @if (testResult(); as t) {
            <p class="test" [class.ok]="t.ok" role="status">
              {{ t.ok ? '✓' : '✕' }} {{ t.message }}
            </p>
          }
        } @else if (loading()) {
          <p class="muted">載入中…</p>
        }

        @if (message()) {
          <p class="muted" role="status">{{ message() }}</p>
        }
        @if (error()) {
          <p class="error" role="alert">{{ error() }}</p>
        }
      </article>
    </section>
  `,
  styles: `
    :host {
      flex: 1;
      overflow-y: auto;
      padding: 2rem 1rem;
    }
    .admin {
      max-width: 64rem;
      margin: 0 auto;
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }
    h1,
    h2,
    p {
      margin: 0;
    }
    h2 {
      font-size: 1.125rem;
    }
    .small {
      font-size: 0.8125rem;
    }
    .state {
      font-weight: 600;
    }
    .state.on {
      color: var(--accent);
    }
    .redirect {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      padding: 0.625rem 0.75rem;
      border-radius: 0.5rem;
      background: var(--surface-muted);
    }
    .redirect code {
      overflow-wrap: anywhere;
      user-select: all;
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 18rem), 1fr));
      gap: 0.875rem 1rem;
    }
    .grid label {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    .stack label.check {
      flex-direction: row;
      align-items: center;
      align-self: flex-start;
      gap: 0.5rem;
    }
    .check input {
      width: auto;
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.75rem;
    }
    .warn-text {
      color: #b45309;
    }
    .test {
      color: var(--danger);
    }
    .test.ok {
      color: #15803d;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SettingsPage implements OnInit {
  private readonly api = inject(ApiService);

  protected readonly settings = signal<OidcSettings | null>(null);
  protected readonly draft = signal<OidcDraft>({
    enabled: true,
    tenantId: '',
    clientId: '',
    clientSecret: '',
    secretExpiresOn: '',
    adminRole: 'Ymir.Admin',
    displayName: '公司帳號',
  });
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly testResult = signal<OidcTestResult | null>(null);

  protected readonly problem = computed(() =>
    oidcDraftProblem(this.draft(), this.settings()?.hasClientSecret ?? false),
  );
  protected readonly tenantLooksValid = computed(
    () => !oidcDraftProblem({ ...this.draft(), enabled: false }, true)?.includes('Tenant'),
  );
  protected readonly expiry = computed(() =>
    secretExpiry(this.draft().secretExpiresOn || null, localToday()),
  );
  protected readonly sourceLabel = computed(() =>
    oidcSourceLabel(this.settings()?.source ?? 'None'),
  );
  protected readonly secretPlaceholder = computed(() => {
    const s = this.settings();
    if (!s?.hasClientSecret) return '尚未設定';
    if (s.secretSource === 'Deployment') return '已在部署設定（.env）中設定';
    return s.secretUpdatedAt
      ? `已設定（${new Date(s.secretUpdatedAt).toLocaleDateString()} 更新）`
      : '已設定';
  });

  ngOnInit(): void {
    this.api.adminGetOidcSettings().subscribe({
      next: (settings) => {
        this.apply(settings);
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.loading.set(false);
      },
    });
  }

  protected patch(change: Partial<OidcDraft>): void {
    this.draft.update((d) => ({ ...d, ...change }));
  }

  protected save(): void {
    if (this.problem()) return;
    const disabling = !this.draft().enabled && this.settings()?.configured;
    if (disabling && !confirm('停用後，所有人只能用本機帳號登入。確定停用企業帳號登入？')) {
      return;
    }
    this.run(this.api.adminSaveOidcSettings(toSaveOidcRequest(this.draft())), (settings) => {
      this.apply(settings);
      this.message.set(
        settings.configured
          ? '已儲存，企業帳號登入立即使用新設定。'
          : '已儲存，企業帳號登入目前未啟用。',
      );
    });
  }

  protected test(): void {
    this.testResult.set(null);
    this.run(this.api.adminTestOidcSettings(this.draft().tenantId.trim()), (result) =>
      this.testResult.set(result),
    );
  }

  protected reset(): void {
    if (
      !confirm('刪除在管理介面儲存的 Entra 設定（含 client secret），改回使用部署設定（.env）？')
    ) {
      return;
    }
    this.run(this.api.adminResetOidcSettings(), (settings) => {
      this.apply(settings);
      this.message.set('已還原為部署設定。');
    });
  }

  private apply(settings: OidcSettings): void {
    this.settings.set(settings);
    this.draft.set(oidcDraftFrom(settings));
  }

  private run<T>(request: Observable<T>, done: (value: T) => void): void {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    request.subscribe({
      next: (value) => {
        done(value);
        this.busy.set(false);
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.busy.set(false);
      },
    });
  }
}
