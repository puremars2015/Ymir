import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  OnInit,
  output,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { AdminUser, UserModelAccess } from '../../core/api/api-types';
import { ModelStore } from '../../core/models/model.store';
import {
  ModelDraft,
  ModelGrant,
  modelDraft,
  modelEnabled,
  modelOverrides,
} from '../../core/admin/user-model-rules';

@Component({
  selector: 'app-user-model-card',
  imports: [FormsModule],
  template: `
    <form class="stack" (ngSubmit)="save()">
      <h2>{{ user().displayName }} 的模型權限</h2>
      <p class="muted">預設依系統設定。可為這位使用者額外開放模型，或禁止使用已開放的模型。</p>
      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }
      @if (state(); as s) {
        @if (!s.isValid) {
          <p class="error" role="alert">既有設定無法讀取，目前已停止開放模型；請重新儲存或還原。</p>
        }
        <div class="models">
          @for (model of s.models; track model.id) {
            <label class="model-row">
              <span
                >{{ model.displayName
                }}<small class="muted"
                  >系統：{{ model.systemEnabled ? '開放' : '未開放' }}</small
                ></span
              >
              <select
                [name]="model.id"
                [attr.aria-label]="model.displayName + ' 模型權限'"
                [ngModel]="draft()[model.id]"
                (ngModelChange)="patch(model.id, $event)"
                [disabled]="busy()"
              >
                <option value="Inherit">
                  依系統設定（{{ model.systemEnabled ? '開放' : '未開放' }}）
                </option>
                <option value="Allow">允許</option>
                <option value="Deny">不允許</option>
              </select>
              <span [class.danger-text]="!enabled(draft()[model.id], model.systemEnabled)"
                >結果：{{
                  enabled(draft()[model.id], model.systemEnabled) ? '可使用' : '不可使用'
                }}</span
              >
            </label>
          }
        </div>
        @if (noneEnabled()) {
          <p class="muted" role="status">沒有可使用的模型，這位使用者將無法送出新訊息。</p>
        }
        <p class="muted small">新訊息及尚未開始的執行會套用最新權限；已開始的回覆會繼續完成。</p>
      } @else {
        <p class="muted">載入中…</p>
      }
      <div class="actions">
        <button type="submit" [disabled]="busy() || !state()">儲存模型權限</button>
        <button type="button" class="secondary" [disabled]="busy() || !state()" (click)="reset()">
          全部依系統設定
        </button>
        <button type="button" class="secondary" [disabled]="busy()" (click)="closed.emit()">
          取消
        </button>
      </div>
    </form>
  `,
  styles: `
    :host {
      display: block;
      padding: 0.75rem 0;
    }
    h2 {
      margin: 0;
      font-size: 1.125rem;
    }
    p {
      margin: 0;
    }
    .models {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }
    .model-row {
      display: grid;
      grid-template-columns: minmax(10rem, 1fr) minmax(12rem, 1fr) auto;
      align-items: center;
      gap: 1rem;
    }
    small {
      display: block;
      margin-top: 0.25rem;
    }
    .actions {
      display: flex;
      gap: 0.5rem;
      flex-wrap: wrap;
    }
    @media (max-width: 640px) {
      .model-row {
        grid-template-columns: 1fr;
        gap: 0.375rem;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserModelCard implements OnInit {
  readonly user = input.required<AdminUser>();
  readonly closed = output<void>();
  readonly saved = output<string>();
  private readonly api = inject(ApiService);
  private readonly models = inject(ModelStore);
  protected readonly state = signal<UserModelAccess | null>(null);
  protected readonly draft = signal<ModelDraft>({});
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly enabled = modelEnabled;
  protected readonly noneEnabled = computed(
    () => !this.state()?.models.some((m) => modelEnabled(this.draft()[m.id], m.systemEnabled)),
  );

  ngOnInit(): void {
    this.api.adminGetUserModelAccess(this.user().id).subscribe({
      next: (s) => {
        this.state.set(s);
        this.draft.set(modelDraft(s));
      },
      error: (e: unknown) => this.error.set(describeApiError(e)),
    });
  }
  protected patch(id: string, grant: ModelGrant): void {
    this.draft.update((d) => ({ ...d, [id]: grant }));
  }
  protected save(): void {
    this.update(false);
  }
  protected reset(): void {
    this.update(true);
  }
  private update(reset: boolean): void {
    if (this.busy() || !this.state()) return;
    this.busy.set(true);
    this.error.set(null);
    const request = reset
      ? this.api.adminResetUserModelAccess(this.user().id)
      : this.api.adminSaveUserModelAccess(this.user().id, {
          overrides: modelOverrides(this.draft()),
        });
    request.subscribe({
      next: () => {
        this.models.load();
        this.saved.emit(`已${reset ? '還原' : '更新'} ${this.user().displayName} 的模型權限。`);
        this.closed.emit();
      },
      error: (e: unknown) => {
        this.busy.set(false);
        this.error.set(describeApiError(e));
      },
    });
  }
}
