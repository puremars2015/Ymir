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
import { oidcSourceLabel } from '../../core/admin/oidc-settings-rules';
import {
  formatBudget,
  formatLimit,
  formatMinutes,
  POLICY_LIMITS,
  policyDraftFrom,
  policyDraftProblem,
  policySummary,
  RuntimePolicyDraft,
  toSavePolicyRequest,
} from '../../core/admin/runtime-policy-rules';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { RuntimePolicy } from '../../core/api/api-types';

/**
 * 系統設定：執行環境（ADR-0011）。閒置停止、單次執行上限與每人配額；存檔後立即生效，不必重新啟動。
 */
@Component({
  selector: 'app-runtime-settings-card',
  imports: [DatePipe, FormsModule],
  template: `
    <article class="card stack">
      <header>
        <h2>執行環境</h2>
        @if (policy(); as p) {
          <p class="muted small summary">{{ summary() }}</p>
          <p class="muted small">
            設定來源：{{ sourceLabel() }}
            @if (p.updatedAt) {
              · {{ p.updatedAt | date: 'yyyy-MM-dd HH:mm' }}
              {{ p.updatedByName ? '由 ' + p.updatedByName + ' 更新' : '' }}
            }
          </p>
        }
      </header>

      @if (policy(); as p) {
        <form class="stack" (ngSubmit)="save()">
          <div class="grid">
            <label
              >閒置停止時間（分鐘）
              <input
                name="idleTimeoutMinutes"
                type="number"
                inputmode="numeric"
                [min]="limits.idleTimeoutMinutes.min"
                [max]="limits.idleTimeoutMinutes.max"
                [ngModel]="draft().idleTimeoutMinutes"
                (ngModelChange)="patch({ idleTimeoutMinutes: $event + '' })"
              />
              <span class="muted small"
                >沒有工作多久後停止使用者的執行環境（檔案保留，下次送訊息時自動啟動）。0
                表示不自動停止。部署設定：{{
                  minutes(p.deployment.idleTimeoutMinutes, '不自動停止')
                }}</span
              >
            </label>
            <label
              >單次執行上限（分鐘）
              <input
                name="executionTimeoutMinutes"
                type="number"
                inputmode="numeric"
                [min]="limits.executionTimeoutMinutes.min"
                [max]="limits.executionTimeoutMinutes.max"
                [ngModel]="draft().executionTimeoutMinutes"
                (ngModelChange)="patch({ executionTimeoutMinutes: $event + '' })"
              />
              <span class="muted small"
                >Agent 一次最多工作多久，超過就停止並告知使用者。部署設定：{{
                  minutes(p.deployment.executionTimeoutMinutes)
                }}</span
              >
            </label>
            <label
              >每人同時排隊的工作數
              <input
                name="maxPendingExecutionsPerUser"
                type="number"
                inputmode="numeric"
                [min]="limits.maxPendingExecutionsPerUser.min"
                [max]="limits.maxPendingExecutionsPerUser.max"
                [ngModel]="draft().maxPendingExecutionsPerUser"
                (ngModelChange)="patch({ maxPendingExecutionsPerUser: $event + '' })"
              />
              <span class="muted small"
                >同一人的工作依序執行；排隊加執行中超過這個數量就不能再送。部署設定：{{
                  p.deployment.maxPendingExecutionsPerUser
                }}</span
              >
            </label>
            <label
              >每人每日執行次數
              <input
                name="dailyExecutionLimit"
                type="number"
                inputmode="numeric"
                [min]="limits.dailyExecutionLimit.min"
                [max]="limits.dailyExecutionLimit.max"
                [ngModel]="draft().dailyExecutionLimit"
                (ngModelChange)="patch({ dailyExecutionLimit: $event + '' })"
              />
              <span class="muted small"
                >過去 24 小時內最多送出幾次。0 表示不限制。部署設定：{{
                  limit(p.deployment.dailyExecutionLimit)
                }}</span
              >
            </label>
            <label
              >每人每月模型預算（US$）
              <input
                name="monthlyBudgetUsd"
                type="number"
                inputmode="decimal"
                step="0.01"
                [min]="limits.monthlyBudgetUsd.min"
                [max]="limits.monthlyBudgetUsd.max"
                [ngModel]="draft().monthlyBudgetUsd"
                (ngModelChange)="patch({ monthlyBudgetUsd: $event + '' })"
              />
              <span class="muted small"
                >每 30 天一期，由 LiteLLM 依設定的模型單價計費並強制，用完就無法呼叫模型。0
                表示不限制。部署設定：{{ budget(p.deployment.monthlyBudgetUsd) }}</span
              >
            </label>
          </div>
          <div class="row">
            <button type="submit" [disabled]="busy() || !!problem()">儲存</button>
            @if (p.source === 'Database') {
              <button type="button" class="link" [disabled]="busy()" (click)="reset()">
                還原為部署設定
              </button>
            }
            @if (problem(); as message) {
              <span class="muted small">{{ message }}</span>
            }
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
    .summary {
      margin-top: 0.25rem;
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 18rem), 1fr));
      gap: 0.875rem 1rem;
    }
    label {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.75rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RuntimeSettingsCard implements OnInit {
  private readonly api = inject(ApiService);

  protected readonly limits = POLICY_LIMITS;
  protected readonly policy = signal<RuntimePolicy | null>(null);
  protected readonly draft = signal<RuntimePolicyDraft>({
    idleTimeoutMinutes: '',
    executionTimeoutMinutes: '',
    maxPendingExecutionsPerUser: '',
    dailyExecutionLimit: '',
    monthlyBudgetUsd: '',
  });
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);

  protected readonly problem = computed(() => policyDraftProblem(this.draft()));
  protected readonly summary = computed(() => {
    const p = this.policy();
    return p ? policySummary(p) : '';
  });
  protected readonly sourceLabel = computed(() =>
    oidcSourceLabel(this.policy()?.source ?? 'Deployment'),
  );

  ngOnInit(): void {
    this.api.adminGetRuntimePolicy().subscribe({
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

  protected patch(change: Partial<RuntimePolicyDraft>): void {
    this.draft.update((d) => ({ ...d, ...change }));
  }

  protected minutes(value: number | string, zeroLabel?: string): string {
    return formatMinutes(value, zeroLabel);
  }

  protected budget(value: number | string): string {
    return formatBudget(value);
  }

  protected limit(value: number | string): string {
    return formatLimit(value, '次');
  }

  protected save(): void {
    if (this.problem()) return;
    this.run(this.api.adminSaveRuntimePolicy(toSavePolicyRequest(this.draft())), (policy) => {
      this.apply(policy);
      this.message.set('已儲存，新的執行政策立即生效。');
    });
  }

  protected reset(): void {
    if (!confirm('刪除在管理介面儲存的執行政策，改回使用部署設定（.env）？')) {
      return;
    }
    this.run(this.api.adminResetRuntimePolicy(), (policy) => {
      this.apply(policy);
      this.message.set('已還原為部署設定。');
    });
  }

  private apply(policy: RuntimePolicy): void {
    this.policy.set(policy);
    this.draft.set(policyDraftFrom(policy.effective));
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
