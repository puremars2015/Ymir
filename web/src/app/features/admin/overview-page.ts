import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  browserUtcOffsetMinutes,
  canStopRuntime,
  healthLevel,
  healthName,
  healthWarnings,
  runtimeStatusLabel,
  trendBars,
} from '../../core/admin/admin-rules';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { localToday, oidcWarnings } from '../../core/admin/oidc-settings-rules';
import { tunnelWarnings } from '../../core/admin/tunnel-settings-rules';
import {
  AdminOverview,
  OidcSettings,
  RuntimeSummary,
  ServiceHealth,
  TunnelSettings,
} from '../../core/api/api-types';
import { AdminTabs } from './admin-tabs';

/** Admin 總覽（ADR-0010）：使用者、Agent 執行狀況、各使用者的執行環境。 */
@Component({
  selector: 'app-overview-page',
  imports: [AdminTabs, DatePipe, RouterLink],
  template: `
    <section class="admin">
      <app-admin-tabs />
      <header class="head">
        <h1>總覽</h1>
        <button type="button" class="secondary" [disabled]="loading()" (click)="load()">
          重新整理
        </button>
      </header>

      @for (warning of warnings(); track warning.message) {
        <a class="warning" [attr.data-level]="warning.level" [routerLink]="warning.link">
          <span aria-hidden="true">{{ warning.level === 'danger' ? '⛔' : '⚠️' }}</span>
          {{ warning.message }}
        </a>
      }
      @if (message()) {
        <p class="muted" role="status">{{ message() }}</p>
      }
      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }

      @if (overview(); as o) {
        <div class="stats">
          <article class="card stat">
            <span class="label">使用者</span>
            <strong class="value">{{ o.users.active }}</strong>
            <span class="muted small"
              >共 {{ o.users.total }} 人 · 停用 {{ o.users.disabled }} · 管理員
              {{ o.users.admins }}</span
            >
          </article>
          <article class="card stat">
            <span class="label">24 小時內登入</span>
            <strong class="value">{{ o.users.recentlyActive }}</strong>
            <span class="muted small">人</span>
          </article>
          <article class="card stat" [class.live]="+o.executions.running > 0">
            <span class="label">Agent 執行中</span>
            <strong class="value">{{ o.executions.running }}</strong>
            <span class="muted small">排隊中 {{ o.executions.queued }}</span>
          </article>
          <article class="card stat">
            <span class="label">今日執行</span>
            <strong class="value">{{ todayTotal() }}</strong>
            <span class="muted small"
              >完成 {{ o.executions.completedToday }} ·
              <span [class.danger-text]="+o.executions.failedToday > 0"
                >失敗 {{ o.executions.failedToday }}</span
              >
              · 取消 {{ o.executions.cancelledToday }}</span
            >
          </article>
        </div>

        @if (health(); as h) {
          <article class="card services">
            <h2>服務狀態</h2>
            <ul class="checks">
              @for (check of h.checks; track check.name) {
                <li [attr.data-level]="level(check.status)">
                  <span class="dot" aria-hidden="true">●</span>
                  <strong>{{ name(check.name) }}</strong>
                  <span class="muted small">{{ check.description }}</span>
                </li>
              }
            </ul>
          </article>
        }

        <article class="card trend">
          <h2>近 7 天執行數</h2>
          <div class="bars" role="img" [attr.aria-label]="trendLabel()">
            @for (bar of bars(); track bar.date) {
              <div
                class="bar-col"
                [title]="bar.label + '：' + bar.total + ' 次，失敗 ' + bar.failed"
              >
                <span class="count">{{ bar.total }}</span>
                <div class="bar-track">
                  <div class="bar" [style.height.%]="bar.height">
                    @if (bar.failed > 0 && bar.total > 0) {
                      <div
                        class="bar-failed"
                        [style.height.%]="(bar.failed / bar.total) * 100"
                      ></div>
                    }
                  </div>
                </div>
                <span class="muted small">{{ bar.label }}</span>
              </div>
            }
          </div>
          <p class="muted small legend">
            <span class="swatch"></span> 執行 <span class="swatch failed"></span> 失敗
          </p>
        </article>

        <article class="card">
          <h2>執行環境</h2>
          <p class="muted small">每位使用者一個 container；狀態以最後一次紀錄為準。</p>
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>使用者</th>
                  <th>狀態</th>
                  <th>最後活動</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                @for (runtime of o.runtimes; track runtime.userId) {
                  <tr>
                    <td>{{ runtime.displayName }}</td>
                    <td>
                      <span class="badge" [attr.data-status]="runtime.status">{{
                        statusLabel(runtime)
                      }}</span>
                    </td>
                    <td class="small">
                      {{
                        runtime.lastActiveAt
                          ? (runtime.lastActiveAt | date: 'yyyy-MM-dd HH:mm')
                          : '—'
                      }}
                    </td>
                    <td>
                      <span class="actions">
                        <a
                          class="link"
                          [routerLink]="['/admin/audit']"
                          [queryParams]="{ userId: runtime.userId, name: runtime.displayName }"
                          >稽核紀錄</a
                        >
                        @if (canStop(runtime)) {
                          <button
                            type="button"
                            class="link danger-text"
                            [disabled]="busy()"
                            (click)="stop(runtime)"
                          >
                            停止
                          </button>
                        }
                      </span>
                    </td>
                  </tr>
                } @empty {
                  <tr>
                    <td colspan="4" class="muted">還沒有任何執行環境</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
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
      align-items: center;
      justify-content: space-between;
    }
    h1,
    h2,
    p {
      margin: 0;
    }
    h2 {
      font-size: 1.0625rem;
      margin-bottom: 0.5rem;
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
    .stat.live .value {
      color: var(--accent);
    }
    .bars {
      display: grid;
      grid-template-columns: repeat(7, 1fr);
      gap: 0.5rem;
      height: 10rem;
    }
    .bar-col {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 0.25rem;
      min-width: 0;
    }
    .count {
      font-size: 0.8125rem;
    }
    .bar-track {
      flex: 1;
      width: 100%;
      max-width: 2.5rem;
      display: flex;
      align-items: flex-end;
    }
    .bar {
      width: 100%;
      min-height: 2px;
      background: var(--accent);
      border-radius: 0.375rem 0.375rem 0 0;
      display: flex;
      align-items: flex-end;
      overflow: hidden;
    }
    .bar-failed,
    .swatch.failed {
      background: var(--danger);
    }
    .bar-failed {
      width: 100%;
    }
    .legend {
      margin-top: 0.5rem;
      display: flex;
      align-items: center;
      gap: 0.375rem;
    }
    .swatch {
      display: inline-block;
      width: 0.75rem;
      height: 0.75rem;
      border-radius: 0.1875rem;
      background: var(--accent);
    }
    .table-wrap {
      overflow-x: auto;
      margin-top: 0.5rem;
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
    }
    th {
      color: var(--text-muted);
      font-weight: 600;
      font-size: 0.8125rem;
    }
    .badge {
      font-size: 0.8125rem;
      padding: 0.0625rem 0.5rem;
      border-radius: 999px;
      border: 1px solid var(--border);
      color: var(--text-muted);
      white-space: nowrap;
    }
    .badge[data-status='Running'],
    .badge[data-status='Busy'] {
      color: var(--accent);
      border-color: var(--accent);
    }
    .badge[data-status='Error'] {
      color: var(--danger);
      border-color: var(--danger);
    }
    .warning {
      display: flex;
      gap: 0.5rem;
      padding: 0.625rem 0.875rem;
      border-radius: 0.625rem;
      border: 1px solid var(--warning);
      background: var(--warning-soft);
      color: var(--text);
      text-decoration: none;
    }
    .warning[data-level='danger'] {
      border-color: var(--danger);
      background: var(--danger-soft);
    }
    .checks {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 16rem), 1fr));
      gap: 0.5rem 1rem;
    }
    .checks li {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      gap: 0.375rem;
    }
    .checks .dot {
      color: var(--success);
    }
    .checks [data-level='warn'] .dot {
      color: var(--warning);
    }
    .checks [data-level='down'] .dot {
      color: var(--danger);
    }
    .actions {
      display: inline-flex;
      gap: 0.75rem;
      white-space: nowrap;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OverviewPage implements OnInit {
  private readonly api = inject(ApiService);

  protected readonly overview = signal<AdminOverview | null>(null);
  private readonly oidc = signal<OidcSettings | null>(null);
  private readonly tunnel = signal<TunnelSettings | null>(null);
  protected readonly health = signal<ServiceHealth | null>(null);
  protected readonly warnings = computed(() => {
    const oidc = this.oidc();
    const tunnel = this.tunnel();
    return [
      ...healthWarnings(this.health()?.checks ?? []).map((message) => ({
        level: 'danger' as const,
        message,
        link: '/admin',
      })),
      ...(tunnel ? tunnelWarnings(tunnel) : []),
      ...(oidc ? oidcWarnings(oidc, localToday()) : []),
    ];
  });
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);

  protected readonly bars = computed(() => trendBars(this.overview()?.executions.trend ?? []));
  protected readonly trendLabel = computed(() =>
    this.bars()
      .map((b) => `${b.label} ${b.total} 次`)
      .join('，'),
  );
  protected readonly todayTotal = computed(() => {
    const e = this.overview()?.executions;
    return e ? Number(e.completedToday) + Number(e.failedToday) + Number(e.cancelledToday) : 0;
  });

  ngOnInit(): void {
    this.load();
    // 警告只是提示：讀不到設定時不影響總覽
    this.api.adminGetOidcSettings().subscribe({
      next: (settings) => this.oidc.set(settings),
      error: () => this.oidc.set(null),
    });
    this.api.adminGetTunnelSettings().subscribe({
      next: (settings) => this.tunnel.set(settings),
      error: () => this.tunnel.set(null),
    });
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.adminHealth().subscribe({
      next: (health) => this.health.set(health),
      error: () => this.health.set(null),
    });
    this.api.adminOverview(browserUtcOffsetMinutes()).subscribe({
      next: (overview) => {
        this.overview.set(overview);
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.loading.set(false);
      },
    });
  }

  protected name(check: string): string {
    return healthName(check);
  }

  protected level(status: string): string {
    return healthLevel(status);
  }

  protected statusLabel(runtime: RuntimeSummary): string {
    return runtimeStatusLabel(runtime.status);
  }

  protected canStop(runtime: RuntimeSummary): boolean {
    return canStopRuntime(runtime.status);
  }

  protected stop(runtime: RuntimeSummary): void {
    if (
      !confirm(
        `確定停止「${runtime.displayName}」的執行環境？進行中的工作會中斷，檔案不會刪除，下次送訊息時會自動重新啟動。`,
      )
    ) {
      return;
    }
    this.busy.set(true);
    this.message.set(null);
    this.error.set(null);
    this.api.adminStopRuntime(runtime.userId).subscribe({
      next: () => {
        this.busy.set(false);
        this.message.set(`已停止「${runtime.displayName}」的執行環境。`);
        this.load();
      },
      error: (e: unknown) => {
        this.busy.set(false);
        this.error.set(describeApiError(e));
      },
    });
  }
}
