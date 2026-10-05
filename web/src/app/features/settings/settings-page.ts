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
import { isSystemPromptTooLong, SYSTEM_PROMPT_MAX_LENGTH } from '../../core/models/model-selection';

/** 個人設定：個人 global system prompt，套用到自己所有的對話（專案的 prompt 會接在後面）。 */
@Component({
  selector: 'app-settings-page',
  imports: [FormsModule],
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
}
