import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  OnInit,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { LoginProviders, UserRole } from '../../core/api/api-types';
import { isLocalPath, loginErrorMessage, oidcLoginUrl } from '../../core/auth/auth-rules';
import { AuthService } from '../../core/auth/auth.service';

/**
 * 登入頁（ADR-0009）：依後端設定顯示
 * - 企業帳號（Entra ID）：整頁導向後端完成 OIDC，瀏覽器不持有任何 token（ADR-0002）；
 * - 本機帳號密碼：給沒有企業帳號的人，由 Admin 建立；
 * - 開發登入：只在 Development。
 */
@Component({
  selector: 'app-login-page',
  imports: [FormsModule],
  template: `
    <section class="card narrow login">
      <h1>登入 Web-Pro Ymir</h1>

      @if (queryError()) {
        <p class="error" role="alert">{{ queryError() }}</p>
      }

      @if (providers(); as p) {
        @if (p.oidc) {
          <a class="button primary sso" [href]="oidcUrl()">以{{ p.oidcDisplayName }}登入</a>
        }

        @if (p.password) {
          @if (p.oidc) {
            <p class="divider"><span>或使用 Ymir 帳號</span></p>
          }
          <form class="stack" (ngSubmit)="passwordLogin()">
            <label>
              帳號
              <input
                name="account"
                [ngModel]="account()"
                (ngModelChange)="account.set($event)"
                autocomplete="username"
                required
              />
            </label>
            <label>
              密碼
              <input
                name="password"
                type="password"
                [ngModel]="password()"
                (ngModelChange)="password.set($event)"
                autocomplete="current-password"
                required
              />
            </label>
            @if (formError()) {
              <p class="error" role="alert">{{ formError() }}</p>
            }
            <button type="submit" [disabled]="!canSubmitPassword() || busy()">登入</button>
          </form>
        }

        @if (p.devLogin) {
          <details class="dev" [open]="!p.oidc && !p.password">
            <summary>開發登入（只在 Development）</summary>
            <form class="stack" (ngSubmit)="devLogin()">
              <label>
                帳號
                <input
                  name="devAccount"
                  [ngModel]="devAccount()"
                  (ngModelChange)="devAccount.set($event)"
                />
              </label>
              <label>
                角色
                <select name="role" [ngModel]="role()" (ngModelChange)="role.set($event)">
                  <option value="User">User</option>
                  <option value="Admin">Admin</option>
                </select>
              </label>
              <button type="submit" class="secondary" [disabled]="!devAccount().trim() || busy()">
                開發登入
              </button>
            </form>
          </details>
        }
      } @else if (loadError()) {
        <p class="error" role="alert">{{ loadError() }}</p>
      } @else {
        <p class="muted">載入中…</p>
      }
    </section>
  `,
  styles: `
    .login {
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }
    .sso {
      display: block;
      text-align: center;
      text-decoration: none;
      padding: 0.75rem 1rem;
    }
    .divider {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      color: var(--text-muted);
      font-size: 0.8125rem;
      margin: 0;
      &::before,
      &::after {
        content: '';
        flex: 1;
        border-top: 1px solid var(--border);
      }
    }
    .dev summary {
      cursor: pointer;
      color: var(--text-muted);
      font-size: 0.875rem;
      margin-bottom: 0.5rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly returnUrl = input<string>();
  /** OIDC callback 失敗時後端導回 `/login?error=...`。 */
  readonly error = input<string>();

  protected readonly providers = signal<LoginProviders | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly account = signal('');
  protected readonly password = signal('');
  protected readonly devAccount = signal('');
  protected readonly role = signal<UserRole>('User');
  protected readonly busy = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly queryError = computed(() => loginErrorMessage(this.error()));
  protected readonly oidcUrl = computed(() => oidcLoginUrl(this.returnUrl()));
  protected readonly canSubmitPassword = computed(
    () => this.account().trim().length > 0 && this.password().length > 0,
  );

  ngOnInit(): void {
    this.api.loginProviders().subscribe({
      next: (providers) => this.providers.set(providers),
      error: (e: unknown) => this.loadError.set(describeApiError(e)),
    });
  }

  protected passwordLogin(): void {
    this.busy.set(true);
    this.formError.set(null);
    this.auth.passwordLogin(this.account().trim(), this.password()).subscribe({
      next: (me) => this.navigateAfterLogin(me.mustChangePassword),
      error: (e: unknown) => {
        this.formError.set(describeApiError(e));
        this.password.set('');
        this.busy.set(false);
      },
    });
  }

  protected devLogin(): void {
    this.busy.set(true);
    this.formError.set(null);
    this.auth.devLogin(this.devAccount().trim(), this.role()).subscribe({
      next: () => this.navigateAfterLogin(false),
      error: (e: unknown) => {
        this.formError.set(describeApiError(e));
        this.busy.set(false);
      },
    });
  }

  private navigateAfterLogin(mustChangePassword: boolean): void {
    const returnUrl = this.returnUrl();
    void this.router.navigateByUrl(
      mustChangePassword ? '/change-password' : isLocalPath(returnUrl) ? returnUrl : '/',
    );
  }
}
