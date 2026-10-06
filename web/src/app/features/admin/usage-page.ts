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
import { RouterLink } from '@angular/router';
import { runtimeStatusLabel } from '../../core/admin/admin-rules';
import { formatRunTime, quotaLevel, successRate, usageTotals } from '../../core/admin/usage-rules';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { AdminUsage, UserUsage } from '../../core/api/api-types';
import { AdminTabs } from './admin-tabs';

/** Admin 用量（ADR-0011）：各使用者的執行數、結果、Agent 執行時間與每日配額的使用狀況。 */
@Component({
  selector: 'app-usage-page',
  imports: [AdminTabs, DatePipe, FormsModule, RouterLink],
  template: `
    <section class="admin">
      <app-admin-tabs />
      <header class="head">
        <h1>用量</h1>
        <label class="range"
          >期間
          <select name="days" [ngModel]="days()" (ngModelChange)="changeDays($event)">
            @for (option of dayOptions; track option) {
              <option [ngValue]="option">近 {{ option }} 天</option>
            }
          </select>
        </label>
      </header>

      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }

      @if (usage(); as u) {
        <div class="stats">
          <article class="card stat">
            <span class="label">使用人數</span>
            <strong class="value">{{ totals().users }}</strong>
          </article>
          <article class="card stat">
            <span class="label">執行次數</span>
            <strong class="value">{{ totals().executions }}</strong>
            <span class="muted small">失敗 {{ totals().failed }}</span>
          </article>
          <article class="card stat">
            <span class="label">Agent 執行時間</span>
            <strong class="value small-value">{{ runTime(totals().runMinutes) }}</strong>
          </article>
          <article class="card stat">
            <span class="label">每日上限</span>
            <strong class="value small-value">{{
              +u.dailyExecutionLimit ? u.dailyExecutionLimit + ' 次' : '不限制'
            }}</strong>
            <a class="muted small" routerLink="/admin/settings">調整執行政策</a>
          </article>
        </div>

        <article class="card">
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>使用者</th>
                  <th class="num">執行</th>
                  <th class="num">成功率</th>
                  <th class="num">失敗 / 取消</th>
                  <th class="num">執行時間</th>
                  <th class="num">24 小時</th>
                  <th>最後執行</th>
                  <th>執行環境</th>
                </tr>
              </thead>
              <tbody>
                @for (row of u.users; track row.userId) {
                  <tr>
                    <td>
                      <a
                        class="link"
                        [routerLink]="['/admin/audit']"
                        [queryParams]="{ userId: row.userId, name: row.displayName }"
                        >{{ row.displayName }}</a
                      >
                    </td>
                    <td class="num">{{ row.executions }}</td>
                    <td class="num">{{ rate(row) }}</td>
                    <td class="num">
                      <span [class.danger-text]="+row.failed > 0">{{ row.failed }}</span> /
                      {{ row.cancelled }}
                    </td>
                    <td class="num">{{ runTime(row.runMinutes) }}</td>
                    <td class="num">
                      <span class="quota" [attr.data-level]="level(row, u)">{{
                        row.last24Hours
                      }}</span>
                    </td>
                    <td class="small">
                      {{ row.lastExecutionAt ? (row.lastExecutionAt | date: 'MM-dd HH:mm') : '—' }}
                    </td>
                    <td class="small">
                      {{ row.runtimeStatus ? statusLabel(row) : '—' }}
                    </td>
                  </tr>
                } @empty {
                  <tr>
                    <td colspan="8" class="muted">這段期間沒有任何執行</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <p class="muted small legend">
            「24 小時」與每日上限比較：<span class="quota" data-level="near">接近上限</span>
            <span class="quota" data-level="reached">已達上限</span>
          </p>
        </article>
      } @else if (loading()) {
        <p class="muted">載入中…</p>
      }
    </section>
  `,
  styles: `
    :host {
      flex: 1;
      overflow-y: auto;
      padding: 2rem 1rem;
    }
    .admin {
      max-width: 64rem;
      margin: 0 auto;
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }
    .head {
      display: flex;
      flex-wrap: wrap;
      gap: 0.75rem;
      align-items: center;
      justify-content: space-between;
    }
    h1,
    p {
      margin: 0;
    }
    .range {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-size: 0.875rem;
    }
    .small {
      font-size: 0.8125rem;
    }
    .stats {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 9.5rem), 1fr));
      gap: 0.75rem;
    }
    .stat {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    .stat .label {
      color: var(--text-muted);
      font-size: 0.875rem;
    }
    .stat .value {
      font-size: 1.875rem;
      line-height: 1.2;
    }
    .stat .small-value {
      font-size: 1.25rem;
    }
    .table-wrap {
      overflow-x: auto;
    }
    table {
      width: 100%;
      border-collapse: collapse;
      font-size: 0.9375rem;
    }
    th,
    td {
      text-align: left;
      padding: 0.5rem 0.625rem;
      border-bottom: 1px solid var(--border);
      white-space: nowrap;
    }
    th {
      color: var(--text-muted);
      font-weight: 600;
      font-size: 0.8125rem;
    }
    .num {
      text-align: right;
    }
    .quota {
      padding: 0 0.375rem;
      border-radius: 999px;
    }
    .quota[data-level='near'] {
      background: rgb(245 158 11 / 18%);
    }
    .quota[data-level='reached'] {
      background: rgb(220 38 38 / 15%);
      color: var(--danger);
      font-weight: 600;
    }
    .legend {
      margin-top: 0.5rem;
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsagePage implements OnInit {
  private readonly api = inject(ApiService);

  protected readonly dayOptions = [1, 7, 30, 90];
  protected readonly days = signal(7);
  protected readonly usage = signal<AdminUsage | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly totals = computed(() => usageTotals(this.usage()?.users ?? []));

  ngOnInit(): void {
    this.load();
  }

  protected changeDays(days: number): void {
    this.days.set(days);
    this.load();
  }

  protected runTime(minutes: number | string): string {
    return formatRunTime(minutes);
  }

  protected rate(row: UserUsage): string {
    const rate = successRate(row);
    return rate === null ? '—' : `${rate}%`;
  }

  protected level(row: UserUsage, usage: AdminUsage): string {
    return quotaLevel(row.last24Hours, usage.dailyExecutionLimit);
  }

  protected statusLabel(row: UserUsage): string {
    return row.runtimeStatus ? runtimeStatusLabel(row.runtimeStatus) : '—';
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.adminUsage(this.days()).subscribe({
      next: (usage) => {
        this.usage.set(usage);
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.loading.set(false);
      },
    });
  }
}
