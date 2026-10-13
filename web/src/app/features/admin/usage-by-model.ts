import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { formatUsd } from '../../core/admin/runtime-policy-rules';
import {
  formatTokens,
  ModelUsageTotal,
  modelKindLabel,
  modelTotals,
  spendShare,
  userModelRows,
} from '../../core/admin/usage-rules';
import { AdminUsage, ModelUsageKind } from '../../core/api/api-types';
import { withProvider } from '../../core/models/model-label';

/**
 * 用量頁的「依模型」卡片（ADR-0021）：所有使用者合計的模型用量，以及每人每模型的明細。
 * 執行次數來自 Ymir，模型請求、token 與費用來自 LiteLLM（Ymir 不保存，ADR-0004 補充）；
 * 沒有連接 LiteLLM 時只顯示執行次數。只用文字綁定，不以 innerHTML 顯示模型名稱。
 */
@Component({
  selector: 'app-usage-by-model',
  imports: [NgTemplateOutlet],
  template: `
    <article class="card">
      <h2>依模型</h2>
      @if (rows().length === 0) {
        <p class="muted">這段期間沒有模型用量</p>
      } @else {
        <div class="table-wrap">
          <table data-table="models">
            <thead>
              <tr>
                <th>模型</th>
                <th class="num">使用人數</th>
                <th class="num">執行（失敗）</th>
                @if (usage().modelUsageAvailable) {
                  <th class="num">模型請求</th>
                  <th class="num">Token（輸入 / 輸出）</th>
                  <th class="num">費用</th>
                  <th class="num">佔比</th>
                }
              </tr>
            </thead>
            <tbody>
              @for (total of totals(); track total.modelId) {
                <tr>
                  <td [attr.title]="hint(total.kind)">
                    <ng-container
                      [ngTemplateOutlet]="modelCell"
                      [ngTemplateOutletContext]="{ $implicit: total }"
                    />
                  </td>
                  <td class="num">{{ total.users }}</td>
                  <td class="num">
                    {{ total.executions
                    }}<span [class.danger-text]="total.failed > 0">（{{ total.failed }}）</span>
                  </td>
                  @if (usage().modelUsageAvailable) {
                    <td class="num">{{ count(total.modelRequests) }}</td>
                    <td class="num small">
                      @if (total.promptTokens === null) {
                        —
                      } @else {
                        {{ tokens(total.promptTokens) }} / {{ tokens(total.completionTokens) }}
                      }
                    </td>
                    <td class="num">{{ usd(total.spendUsd) }}</td>
                    <td class="num">{{ share(total) }}</td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>

        <div class="table-wrap">
          <table data-table="user-models">
            <thead>
              <tr>
                <th>使用者</th>
                <th>模型</th>
                <th class="num">執行（失敗）</th>
                @if (usage().modelUsageAvailable) {
                  <th class="num">模型請求</th>
                  <th class="num">Token（輸入 / 輸出）</th>
                  <th class="num">費用</th>
                }
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.userId + '/' + (row.model.modelId ?? '')) {
                <tr>
                  @if (row.span > 0) {
                    <td class="user" [attr.rowspan]="row.span">{{ row.displayName }}</td>
                  }
                  <td [attr.title]="hint(row.model.kind)">
                    <ng-container
                      [ngTemplateOutlet]="modelCell"
                      [ngTemplateOutletContext]="{ $implicit: row.model }"
                    />
                  </td>
                  <td class="num">
                    {{ row.model.executions
                    }}<span [class.danger-text]="+row.model.failed > 0"
                      >（{{ row.model.failed }}）</span
                    >
                  </td>
                  @if (usage().modelUsageAvailable) {
                    <td class="num">{{ count(row.model.modelRequests) }}</td>
                    <td class="num small">
                      @if (row.model.promptTokens === null) {
                        —
                      } @else {
                        {{ tokens(row.model.promptTokens) }} /
                        {{ tokens(row.model.completionTokens) }}
                      }
                    </td>
                    <td class="num">{{ usd(row.model.spendUsd) }}</td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
      <p class="muted small">
        執行次數來自 Ymir（近 {{ usage().days }}×24 小時）；模型請求、token 與費用來自 LiteLLM，依
        UTC 日期計算（含今天共 {{ usage().days }} 天），包含知識庫的 embedding 與問答。
      </p>
    </article>

    <ng-template #modelCell let-model>
      <span class="model">
        <span>{{ modelName(model) }}</span>
        @if (model.modelId !== null && model.modelId !== model.displayName) {
          <span class="muted small">{{ model.modelId }}</span>
        }
        @if (kindLabel(model.kind); as label) {
          <span class="tag">{{ label }}</span>
        }
      </span>
    </ng-template>
  `,
  styles: `
    :host {
      display: block;
    }
    .card {
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }
    h2,
    p {
      margin: 0;
    }
    h2 {
      font-size: 1.125rem;
    }
    .small {
      font-size: 0.8125rem;
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
    .user {
      vertical-align: top;
    }
    .model {
      display: inline-flex;
      flex-wrap: wrap;
      align-items: baseline;
      gap: 0.375rem;
    }
    .tag {
      font-size: 0.75rem;
      padding: 0 0.5rem;
      border-radius: 999px;
      border: 1px solid var(--border);
      color: var(--text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsageByModel {
  readonly usage = input.required<AdminUsage>();

  protected readonly totals = computed(() => modelTotals(this.usage().users));
  protected readonly rows = computed(() => userModelRows(this.usage().users));
  protected readonly totalSpend = computed(() =>
    this.totals().reduce((sum, total) => sum + (total.spendUsd ?? 0), 0),
  );

  /** 用量頁是管理介面，模型名稱帶 provider（ADR-0020 補充）。 */
  protected modelName(model: { displayName: string; provider?: string | null }): string {
    return withProvider(model.displayName, model.provider);
  }

  protected kindLabel(kind: ModelUsageKind): string | null {
    return modelKindLabel(kind);
  }

  /** 未標示模型的來源說明：舊執行沒有保存模型，或 LiteLLM 既沒有 model group 也沒有模型名稱的紀錄（ADR-0021）。 */
  protected hint(kind: ModelUsageKind): string | null {
    return kind === 'Unattributed' ? '舊的執行紀錄沒有保存模型，或 LiteLLM 未標示模型的請求' : null;
  }

  /** null 表示沒有 LiteLLM 資料（未連接或這位使用者查詢失敗），與 0 區分。 */
  protected count(value: number | string | null): string {
    return value === null ? '—' : String(value);
  }

  protected usd(value: number | string | null): string {
    return value === null ? '—' : formatUsd(value);
  }

  protected tokens(value: number | string | null): string {
    return formatTokens(value);
  }

  protected share(total: ModelUsageTotal): string {
    const share = spendShare(total.spendUsd, this.totalSpend());
    return share === null ? '—' : `${share}%`;
  }
}
