import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CAPABILITIES, CapabilityKey, capabilitySummary } from '../../core/admin/extension-rules';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { ExtensionPolicy, ExtensionValues } from '../../core/api/api-types';

/**
 * 系統設定：Agent 擴充能力的全域預設（ADR-0012）。個別成員的覆寫在「使用者」頁設定。
 * 政策在下一次執行時由伺服器套用；沒有開放時，使用者放的 skill / MCP 設定保留但不會被載入。
 */
@Component({
  selector: 'app-extension-settings-card',
  imports: [DatePipe, FormsModule],
  template: `
    <article class="card stack">
      <header>
        <h2>Agent 擴充能力</h2>
        @if (policy(); as p) {
          <p class="muted small summary">全域預設：{{ summary(p.defaults) }}</p>
          @if (p.updatedAt) {
            <p class="muted small">
              {{ p.updatedAt | date: 'yyyy-MM-dd HH:mm' }}
              {{ p.updatedByName ? '由 ' + p.updatedByName + ' 更新' : '' }}
            </p>
          }
        }
      </header>

      @if (policy()) {
        <form class="stack" (ngSubmit)="save()">
          @for (c of capabilities; track c.key) {
            <label class="check">
              <input
                type="checkbox"
                [name]="c.key"
                [attr.name]="c.key"
                [ngModel]="draft()[c.key]"
                (ngModelChange)="patch(c.key, $event)"
              />
              <span>
                允許{{ c.label }}
                <span class="muted small block">{{ c.description }}</span>
              </span>
            </label>
          }
          <p class="muted small">
            沒有個人設定的成員套用這裡的預設；要針對個人開放或禁止，到「使用者」頁設定。變更從成員的下一則訊息開始生效；
            關閉時，成員已建立的 skill 與 MCP 設定會保留，只是不會被載入。
          </p>
          <div class="row">
            <button type="submit" [disabled]="busy()">儲存</button>
          </div>
        </form>
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
  `,
  styles: `
    h2 {
      margin: 0;
      font-size: 1.125rem;
    }
    p {
      margin: 0;
    }
    .small {
      font-size: 0.8125rem;
    }
    .block {
      display: block;
    }
    .summary {
      margin-top: 0.25rem;
    }
    .check {
      display: flex;
      flex-direction: row;
      gap: 0.5rem;
      align-items: flex-start;
    }
    .check input {
      margin-top: 0.25rem;
    }
    .row {
      display: flex;
      gap: 0.75rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ExtensionSettingsCard implements OnInit {
  private readonly api = inject(ApiService);

  protected readonly capabilities = CAPABILITIES;
  protected readonly policy = signal<ExtensionPolicy | null>(null);
  protected readonly draft = signal<ExtensionValues>({ skills: false, mcp: false });
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);

  ngOnInit(): void {
    this.api.adminGetExtensionPolicy().subscribe({
      next: (policy) => {
        this.apply(policy);
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.loading.set(false);
      },
    });
  }

  protected summary(values: ExtensionValues): string {
    return capabilitySummary(values);
  }

  protected patch(key: CapabilityKey, value: boolean): void {
    this.draft.update((d) => ({ ...d, [key]: value }));
  }

  protected save(): void {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    this.api.adminSaveExtensionPolicy(this.draft()).subscribe({
      next: (policy) => {
        this.apply(policy);
        this.message.set('已儲存，從成員的下一則訊息開始生效。');
        this.busy.set(false);
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.busy.set(false);
      },
    });
  }

  private apply(policy: ExtensionPolicy): void {
    this.policy.set(policy);
    this.draft.set({ ...policy.defaults });
  }
}
