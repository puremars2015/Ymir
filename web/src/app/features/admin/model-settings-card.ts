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
import { ModelAccess } from '../../core/api/api-types';
import { ModelStore } from '../../core/models/model.store';

@Component({
  selector: 'app-model-settings-card',
  imports: [FormsModule],
  template: ` <article class="card stack">
    <h2>開放模型</h2>
    <p class="muted">
      勾選使用者可以使用的模型，並指定預設模型。儲存後套用至新工作，已開始的工作會繼續完成。
    </p>
    @if (settings(); as s) {
      <p class="muted">設定來源：{{ s.usesDeployment ? '部署設定' : '管理員設定' }}</p>
      <form class="stack" (ngSubmit)="save()">
        @for (model of s.models; track model.id) {
          <label class="model-row">
            <input
              type="checkbox"
              [name]="model.id"
              [ngModel]="enabled().includes(model.id)"
              (ngModelChange)="toggle(model.id, $event)"
              [disabled]="busy()"
            />
            <span>{{ name(model.displayName) }}</span>
          </label>
        }
        <label
          >預設模型
          <select
            name="defaultModel"
            [ngModel]="defaultId()"
            (ngModelChange)="defaultId.set($event); saved.set(false)"
            [disabled]="busy()"
          >
            @for (model of s.models; track model.id) {
              @if (enabled().includes(model.id)) {
                <option [value]="model.id">{{ name(model.displayName) }}</option>
              }
            }
          </select>
        </label>
        @if (!valid()) {
          <p class="error">至少開放一個模型，且預設模型必須在開放清單內。</p>
        }
        <div class="actions">
          <button type="submit" [disabled]="busy() || !valid()">
            {{ busy() ? '儲存中…' : '儲存模型設定' }}
          </button>
          <button
            type="button"
            class="secondary"
            [disabled]="busy() || s.usesDeployment"
            (click)="reset()"
          >
            還原模型部署設定
          </button>
        </div>
      </form>
    } @else if (!error()) {
      <p class="muted" role="status">載入模型設定…</p>
    }
    @if (error()) {
      <p class="error" role="alert">
        {{ error() }} <button type="button" class="link" (click)="load()">重新載入</button>
      </p>
    }
    @if (saved()) {
      <p class="saved" role="status">模型設定已儲存。</p>
    }
  </article>`,
  styles: `
    :host {
      display: block;
    }
    h2 {
      margin: 0;
    }
    .model-row {
      display: flex;
      flex-direction: row;
      align-items: center;
      gap: 0.625rem;
      padding: 0.5rem 0;
    }
    .model-row input {
      width: 1.125rem;
      height: 1.125rem;
      accent-color: var(--accent);
    }
    select {
      max-width: 100%;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
    .saved {
      color: var(--success);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ModelSettingsCard implements OnInit {
  private readonly api = inject(ApiService);
  private readonly modelStore = inject(ModelStore);
  protected readonly settings = signal<ModelAccess | null>(null);
  protected readonly enabled = signal<readonly string[]>([]);
  protected readonly defaultId = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly saved = signal(false);
  protected readonly valid = computed(
    () => this.enabled().length > 0 && this.enabled().includes(this.defaultId()),
  );
  ngOnInit(): void {
    this.load();
  }
  protected name(value: string): string {
    return value.replace(/\s*[（(]OpenRouter[）)]\s*$/i, '').trim();
  }
  protected load(): void {
    this.api.getModelAccess().subscribe({
      next: (s) => this.apply(s),
      error: (e: unknown) => this.error.set(describeApiError(e)),
    });
  }
  private apply(s: ModelAccess): void {
    this.settings.set(s);
    this.enabled.set(s.models.filter((m) => m.enabled).map((m) => m.id));
    this.defaultId.set(s.defaultModelId ?? '');
    this.error.set(null);
  }
  protected toggle(id: string, on: boolean): void {
    this.enabled.update((ids) => (on ? [...ids, id] : ids.filter((m) => m !== id)));
    if (!this.enabled().includes(this.defaultId())) this.defaultId.set(this.enabled()[0] ?? '');
    this.saved.set(false);
  }
  protected save(): void {
    if (!this.valid() || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.saved.set(false);
    this.api
      .saveModelAccess({ enabledModelIds: [...this.enabled()], defaultModelId: this.defaultId() })
      .subscribe({
        next: (s) => this.finish(s),
        error: (e: unknown) => {
          this.busy.set(false);
          this.error.set(describeApiError(e));
        },
      });
  }
  protected reset(): void {
    this.busy.set(true);
    this.error.set(null);
    this.saved.set(false);
    this.api.resetModelAccess().subscribe({
      next: (s) => this.finish(s),
      error: (e: unknown) => {
        this.busy.set(false);
        this.error.set(describeApiError(e));
      },
    });
  }
  private finish(s: ModelAccess): void {
    this.apply(s);
    this.busy.set(false);
    this.saved.set(true);
    this.modelStore.load();
  }
}
