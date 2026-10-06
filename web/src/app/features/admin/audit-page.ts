import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import {
  AUDIT_ACTION_FILTERS,
  AUDIT_RESULT_LABELS,
  AuditFilterForm,
  auditQueryParams,
  describeAuditAction,
  emptyAuditFilter,
} from '../../core/admin/admin-rules';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { AuditLogItem, AuditResult } from '../../core/api/api-types';
import { AdminTabs } from './admin-tabs';

/** 稽核紀錄（ADR-0010）：誰在什麼時候做了什麼。由新到舊，「載入更多」往前翻。 */
@Component({
  selector: 'app-audit-page',
  imports: [AdminTabs, DatePipe, FormsModule],
  template: `
    <section class="admin">
      <app-admin-tabs />
      <h1>稽核紀錄</h1>

      <form class="filters" (ngSubmit)="search()">
        <label
          >動作
          <select
            name="action"
            [ngModel]="filter().action"
            (ngModelChange)="patch({ action: $event })"
          >
            @for (option of actionFilters; track option.value) {
              <option [value]="option.value">{{ option.label }}</option>
            }
          </select>
        </label>
        <label
          >結果
          <select
            name="result"
            [ngModel]="filter().result"
            (ngModelChange)="patch({ result: $event })"
          >
            <option value="">全部</option>
            @for (result of results; track result) {
              <option [value]="result">{{ resultLabels[result] }}</option>
            }
          </select>
        </label>
        <label
          >從
          <input
            type="date"
            name="from"
            [ngModel]="filter().from"
            (ngModelChange)="patch({ from: $event })"
        /></label>
        <label
          >到
          <input
            type="date"
            name="to"
            [ngModel]="filter().to"
            (ngModelChange)="patch({ to: $event })"
        /></label>
        <button type="submit" [disabled]="loading()">查詢</button>
      </form>

      @if (filter().userId) {
        <p class="chip">
          使用者：{{ userName() ?? filter().userId }}
          <button type="button" class="link" aria-label="清除使用者篩選" (click)="clearUser()">
            ×
          </button>
        </p>
      }
      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }

      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>時間</th>
              <th>操作者</th>
              <th>動作</th>
              <th>對象</th>
              <th>結果</th>
            </tr>
          </thead>
          <tbody>
            @for (item of items(); track item.id) {
              <tr>
                <td class="small nowrap">{{ item.timestamp | date: 'yyyy-MM-dd HH:mm:ss' }}</td>
                <td>
                  @if (item.actorUserId) {
                    <button
                      type="button"
                      class="link"
                      (click)="filterUser(item.actorUserId, item.actorName)"
                    >
                      {{ item.actorName }}
                    </button>
                  } @else {
                    <span class="muted">{{ item.actorName }}</span>
                  }
                </td>
                <td>
                  {{ actionLabel(item) }}
                  <br /><code class="muted small">{{ item.action }}</code>
                </td>
                <td class="small">
                  {{ item.targetName ?? item.targetType }}
                  @if (!item.targetName) {
                    <br /><code class="muted">{{ item.targetId }}</code>
                  }
                </td>
                <td>
                  <span class="result" [attr.data-result]="item.result">{{
                    resultLabels[item.result]
                  }}</span>
                </td>
              </tr>
            } @empty {
              <tr>
                <td colspan="5" class="muted">{{ loading() ? '載入中…' : '沒有符合的紀錄' }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>

      @if (nextBefore() !== null) {
        <button type="button" class="secondary more" [disabled]="loading()" (click)="loadMore()">
          載入更多
        </button>
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
    h1,
    p {
      margin: 0;
    }
    .filters {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      gap: 0.75rem;
    }
    .filters label {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      font-size: 0.875rem;
    }
    .chip {
      align-self: flex-start;
      display: inline-flex;
      align-items: center;
      gap: 0.5rem;
      padding: 0.25rem 0.75rem;
      border: 1px solid var(--border);
      border-radius: 999px;
      font-size: 0.875rem;
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
      vertical-align: top;
    }
    th {
      color: var(--text-muted);
      font-weight: 600;
      font-size: 0.8125rem;
    }
    .small {
      font-size: 0.8125rem;
    }
    .nowrap {
      white-space: nowrap;
    }
    code {
      font-size: 0.75rem;
      overflow-wrap: anywhere;
    }
    .result {
      font-size: 0.8125rem;
      white-space: nowrap;
    }
    .result[data-result='Failure'],
    .result[data-result='Denied'] {
      color: var(--danger);
      font-weight: 600;
    }
    .more {
      align-self: center;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly actionFilters = AUDIT_ACTION_FILTERS;
  protected readonly resultLabels = AUDIT_RESULT_LABELS;
  protected readonly results = Object.keys(AUDIT_RESULT_LABELS) as AuditResult[];

  protected readonly filter = signal<AuditFilterForm>(emptyAuditFilter());
  protected readonly userName = signal<string | null>(null);
  protected readonly items = signal<AuditLogItem[]>([]);
  protected readonly nextBefore = signal<number | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    const query = this.route.snapshot.queryParamMap;
    this.filter.update((f) => ({ ...f, userId: query.get('userId') }));
    this.userName.set(query.get('name'));
    this.search();
  }

  protected patch(change: Partial<AuditFilterForm>): void {
    this.filter.update((f) => ({ ...f, ...change }));
  }

  protected actionLabel(item: AuditLogItem): string {
    return describeAuditAction(item.action);
  }

  protected filterUser(userId: string, name: string): void {
    this.patch({ userId });
    this.userName.set(name);
    this.syncUrl();
    this.search();
  }

  protected clearUser(): void {
    this.patch({ userId: null });
    this.userName.set(null);
    this.syncUrl();
    this.search();
  }

  protected search(): void {
    this.fetch(null);
  }

  protected loadMore(): void {
    this.fetch(this.nextBefore());
  }

  private syncUrl(): void {
    const { userId } = this.filter();
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: userId ? { userId, name: this.userName() } : {},
      replaceUrl: true,
    });
  }

  private fetch(before: number | null): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.adminSearchAudit(auditQueryParams(this.filter(), before)).subscribe({
      next: (page) => {
        this.items.update((list) => (before === null ? page.items : [...list, ...page.items]));
        this.nextBefore.set(page.nextBefore === null ? null : Number(page.nextBefore));
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.loading.set(false);
      },
    });
  }
}
