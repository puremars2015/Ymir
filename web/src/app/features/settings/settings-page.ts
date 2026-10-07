import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { AuthService } from '../../core/auth/auth.service';
import { newPasswordProblem, PASSWORD_MIN_LENGTH } from '../../core/auth/auth-rules';
import { MyExtensionsCard } from './my-extensions-card';
import { isSystemPromptTooLong, SYSTEM_PROMPT_MAX_LENGTH } from '../../core/models/model-selection';

/** 個人設定：個人 global system prompt，套用到自己所有的對話（專案的 prompt 會接在後面）。 */
@Component({
  selector: 'app-settings-page',
  imports: [FormsModule, MyExtensionsCard],
  template: `
    <section class="settings">
      <h1>個人設定</h1>
      @if (auth.user(); as user) {
        <p class="muted">{{ user.displayName }}{{ user.email ? ' · ' + user.email : '' }}</p>
      }
      <form class="stack" (ngSubmit)="save()">
        <label>
          個人 system prompt
          <span class="muted hint">
            套用到你所有的對話；在專案中，專案的 system prompt 會接在這段之後。留白表示不使用。
          </span>
          <textarea
            name="systemPrompt"
            rows="10"
            [ngModel]="draft()"
            (ngModelChange)="draft.set($event); saved.set(false)"
            [disabled]="loading()"
            placeholder="例如：請一律用繁體中文回答；程式碼註解也用繁體中文。"
          ></textarea>
          <span class="counter" [class.error]="tooLong()">
            {{ draft().trim().length }} / {{ maxLength }}
          </span>
        </label>
        <div class="actions">
          <button type="submit" [disabled]="loading() || saving() || !dirty() || tooLong()">
            儲存
          </button>
          @if (saved()) {
            <span class="muted" role="status">已儲存</span>
          }
          @if (error()) {
            <span class="error">{{ error() }}</span>
          }
        </div>
      </form>

      @if (auth.user()?.authMethod === 'Local') {
        <form class="stack password" (ngSubmit)="changePassword()">
          <h2>變更密碼</h2>
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
        <p class="muted password">企業帳號的密碼請到公司的帳號系統變更。</p>
      }

      <app-my-extensions-card />
    </section>
  `,
  styles: `
    :host {
      flex: 1;
      overflow-y: auto;
      padding: 2rem 1rem;
    }
    .settings {
      max-width: 46rem;
      margin: 0 auto;
    }
    .hint {
      font-size: 0.8125rem;
    }
    .counter {
      align-self: flex-end;
      font-size: 0.75rem;
      color: var(--text-muted);
      &.error {
        color: var(--danger);
      }
    }
    .actions {
      display: flex;
      align-items: center;
      gap: 0.75rem;
    }
    .password {
      margin-top: 2.5rem;
      h2 {
        font-size: 1.125rem;
        margin: 0;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SettingsPage implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly auth = inject(AuthService);
  protected readonly maxLength = SYSTEM_PROMPT_MAX_LENGTH;

  protected readonly draft = signal('');
  private readonly savedValue = signal('');
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly tooLong = computed(() => isSystemPromptTooLong(this.draft()));
  protected readonly dirty = computed(() => this.draft().trim() !== this.savedValue());

  protected readonly passwordMinLength = PASSWORD_MIN_LENGTH;
  protected readonly currentPassword = signal('');
  protected readonly newPassword = signal('');
  protected readonly confirmPassword = signal('');
  protected readonly changingPassword = signal(false);
  protected readonly passwordMessage = signal<string | null>(null);
  protected readonly passwordError = signal<string | null>(null);

  ngOnInit(): void {
    this.api.getSettings().subscribe({
      next: (settings) => {
        this.draft.set(settings.systemPrompt ?? '');
        this.savedValue.set(settings.systemPrompt ?? '');
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.loading.set(false);
      },
    });
  }

  protected save(): void {
    this.saving.set(true);
    this.saved.set(false);
    this.error.set(null);
    this.api.updateSettings(this.draft()).subscribe({
      next: (settings) => {
        this.draft.set(settings.systemPrompt ?? '');
        this.savedValue.set(settings.systemPrompt ?? '');
        this.saving.set(false);
        this.saved.set(true);
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.saving.set(false);
      },
    });
  }

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
