import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { describeApiError } from '../../core/api/api.service';
import { UserRole } from '../../core/api/api-types';
import { AuthService } from '../../core/auth/auth.service';

/**
 * 開發用登入：輸入帳號即可登入（後端只在 Development 提供 `/api/dev/login`）。
 * Sprint 2 改為企業 SSO 按鈕，由後端完成 OIDC（ADR-0002）。
 */
@Component({
  selector: 'app-login-page',
  imports: [FormsModule],
  template: `
    <section class="card narrow">
      <h1>登入 Vibe Maker</h1>
      <p class="muted">開發模式：輸入任意帳號即可登入。正式環境將使用企業帳號（SSO）。</p>
      <form class="stack" (ngSubmit)="login()">
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
          角色
          <select name="role" [ngModel]="role()" (ngModelChange)="role.set($event)">
            <option value="User">User</option>
            <option value="Admin">Admin</option>
          </select>
        </label>
        @if (error()) {
          <p class="error">{{ error() }}</p>
        }
        <button type="submit" [disabled]="!account().trim() || busy()">登入</button>
      </form>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly returnUrl = input<string>();
  protected readonly account = signal('');
  protected readonly role = signal<UserRole>('User');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected login(): void {
    this.busy.set(true);
    this.error.set(null);
    this.auth.devLogin(this.account().trim(), this.role()).subscribe({
      next: () => void this.router.navigateByUrl(this.returnUrl() || '/'),
      error: (error: unknown) => {
        this.error.set(describeApiError(error));
        this.busy.set(false);
      },
    });
  }
}
