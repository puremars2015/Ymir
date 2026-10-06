import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { describeApiError } from '../../core/api/api.service';
import { newPasswordProblem, PASSWORD_MIN_LENGTH } from '../../core/auth/auth-rules';
import { AuthService } from '../../core/auth/auth.service';

/**
 * 本機帳號第一次登入（或 Admin 重設密碼後）必須先改密碼（ADR-0009）。
 * 改完之前，後端對其他 API 一律回 403 PASSWORD_CHANGE_REQUIRED。
 */
@Component({
  selector: 'app-change-password-page',
  imports: [FormsModule],
  template: `
    <section class="card narrow">
      <h1>變更密碼</h1>
      <p class="muted">
        這是管理員設定的密碼，請先設定只有你知道的新密碼（至少 {{ minLength }} 個字元）。
      </p>
      <form class="stack" (ngSubmit)="submit()">
        <label>
          目前密碼
          <input
            name="current"
            type="password"
            autocomplete="current-password"
            [ngModel]="current()"
            (ngModelChange)="current.set($event)"
            required
          />
        </label>
        <label>
          新密碼
          <input
            name="next"
            type="password"
            autocomplete="new-password"
            [ngModel]="next()"
            (ngModelChange)="next.set($event)"
            required
          />
        </label>
        <label>
          再輸入一次新密碼
          <input
            name="confirm"
            type="password"
            autocomplete="new-password"
            [ngModel]="confirm()"
            (ngModelChange)="confirm.set($event)"
            required
          />
        </label>
        @if (touched() && problem()) {
          <p class="error">{{ problem() }}</p>
        }
        @if (error()) {
          <p class="error" role="alert">{{ error() }}</p>
        }
        <div class="row">
          <button type="submit" [disabled]="busy() || !current()">變更密碼</button>
          <button type="button" class="link" (click)="logout()">登出</button>
        </div>
      </form>
    </section>
  `,
  styles: `
    .row {
      display: flex;
      align-items: center;
      gap: 1rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChangePasswordPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly minLength = PASSWORD_MIN_LENGTH;

  protected readonly current = signal('');
  protected readonly next = signal('');
  protected readonly confirm = signal('');
  protected readonly touched = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly problem = computed(() =>
    newPasswordProblem(this.next(), this.confirm(), this.current()),
  );

  protected submit(): void {
    this.touched.set(true);
    if (this.problem()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.auth.changePassword(this.current(), this.next()).subscribe({
      next: () => void this.router.navigateByUrl('/'),
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.busy.set(false);
      },
    });
  }

  protected logout(): void {
    this.auth.logout().subscribe(() => void this.router.navigate(['/login']));
  }
}
