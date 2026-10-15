import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeApiError } from '../../core/api/api.service';
import { AuthService } from '../../core/auth/auth.service';
import { newPasswordProblem, PASSWORD_MIN_LENGTH } from '../../core/auth/auth-rules';

/** 個人設定「變更密碼」分頁：只有本機帳號可在 Ymir 變更（ADR-0009），企業與 Google 帳號說明到哪裡改。 */
@Component({
  selector: 'app-password-page',
  imports: [FormsModule],
  template: `
    @if (auth.user()?.authMethod === 'Local') {
      <form class="stack" (ngSubmit)="changePassword()">
        <label>
          目前密碼
          <input
            name="currentPassword"
            type="password"
            autocomplete="current-password"
            [ngModel]="currentPassword()"
            (ngModelChange)="currentPassword.set($event)"
          />
        </label>
        <label>
          新密碼（至少 {{ passwordMinLength }} 個字元）
          <input
            name="newPassword"
            type="password"
            autocomplete="new-password"
            [ngModel]="newPassword()"
            (ngModelChange)="newPassword.set($event)"
          />
        </label>
        <label>
          再輸入一次新密碼
          <input
            name="confirmPassword"
            type="password"
            autocomplete="new-password"
            [ngModel]="confirmPassword()"
            (ngModelChange)="confirmPassword.set($event)"
          />
        </label>
        <div class="actions">
          <button type="submit" [disabled]="changingPassword() || !currentPassword()">
            變更密碼
          </button>
          @if (passwordMessage()) {
            <span class="muted" role="status">{{ passwordMessage() }}</span>
          }
          @if (passwordError()) {
            <span class="error">{{ passwordError() }}</span>
          }
        </div>
      </form>
    } @else if (auth.user()?.authMethod === 'Oidc') {
      <p class="muted">企業帳號的密碼請到公司的帳號系統變更。</p>
    } @else if (auth.user()?.authMethod === 'Google') {
      <p class="muted">Google 帳號的密碼請到 Google 帳戶設定變更。</p>
    } @else if (auth.user()) {
      <p class="muted">這個登入方式沒有可在 Ymir 變更的密碼。</p>
    }
  `,
  styles: `
    :host {
      display: block;
    }
    p {
      margin: 0;
    }
    .actions {
      display: flex;
      align-items: center;
      gap: 0.75rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PasswordPage {
  protected readonly auth = inject(AuthService);
  protected readonly passwordMinLength = PASSWORD_MIN_LENGTH;
  protected readonly currentPassword = signal('');
  protected readonly newPassword = signal('');
  protected readonly confirmPassword = signal('');
  protected readonly changingPassword = signal(false);
  protected readonly passwordMessage = signal<string | null>(null);
  protected readonly passwordError = signal<string | null>(null);

  protected changePassword(): void {
    this.passwordMessage.set(null);
    const problem = newPasswordProblem(
      this.newPassword(),
      this.confirmPassword(),
      this.currentPassword(),
    );
    if (problem) {
      this.passwordError.set(problem);
      return;
    }
    this.passwordError.set(null);
    this.changingPassword.set(true);
    this.auth.changePassword(this.currentPassword(), this.newPassword()).subscribe({
      next: () => {
        this.currentPassword.set('');
        this.newPassword.set('');
        this.confirmPassword.set('');
        this.passwordMessage.set('密碼已變更');
        this.changingPassword.set(false);
      },
      error: (e: unknown) => {
        this.passwordError.set(describeApiError(e));
        this.changingPassword.set(false);
      },
    });
  }
}
