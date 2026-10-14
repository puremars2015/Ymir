import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  dataStatusLabel,
  eligibilityLabel,
  formatBytes,
  formatPercent,
  freedBytes,
  isEligible,
  pressureKeyLabel,
  reclaimStateLabel,
  ReclamationDraft,
  reclamationDraftFrom,
  reclamationDraftProblem,
  reclamationSummary,
  toReclamationPolicy,
  triggerLabel,
} from '../../core/admin/resource-rules';
import { ApiService, describeApiError } from '../../core/api/api.service';
import {
  AdminDryRun,
  AdminReclaimRuns,
  AdminResources,
  ReclamationPolicyResponse,
} from '../../core/api/api-types';
import { AdminTabs } from './admin-tabs';

/**
 * Admin「系統資源」（ADR-0025）：主機 CPU / 記憶體 / 磁碟、Ymir 的 container 與 image、自動回收政策、dry run 與回收紀錄。
 * 只管理 Ymir 的物件；查詢失敗的資料顯示「—」與失敗原因，不當作零。
 */
@Component({
  selector: 'app-resources-page',
  imports: [AdminTabs, DatePipe, FormsModule],
  template: `
    <section class="admin">
      <app-admin-tabs />
      <header class="head">
        <h1>系統資源</h1>
        <div class="row">
          @if (snapshot(); as s) {
            <span class="muted small"
              >更新於 {{ s.generatedAt | date: 'yyyy-MM-dd HH:mm:ss' }}</span
            >
          }
          <button type="button" class="secondary" [disabled]="loading()" (click)="refresh()">
            重新整理
          </button>
        </div>
      </header>

      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }

      @if (resources(); as r) {
        @if (!r.supported) {
          <article class="card">
            <p>
              這個部署沒有設定主機維護服務（Ymir:Maintenance），無法顯示主機資源或回收 container /
              image。Windows Docker 與本機開發環境不支援。
            </p>
          </article>
        } @else if (!r.available) {
          <article class="card">
            <p class="error">目前無法連線到主機維護服務；稍後再重新整理。</p>
          </article>
        }
      }

      @if (snapshot(); as s) {
        <article class="card stack">
          <h2>主機資源</h2>
          <p class="muted small">
            整台主機（不是 API 容器）的 CPU、記憶體，以及各 Podman store 所在的檔案系統。狀態：{{
              statusLabel(s.host.status)
            }}
          </p>
          <div class="stats">
            <div class="stat">
              <span class="label">CPU 使用 / 剩餘（{{ s.host.cpuCount }} 核）</span>
              <strong class="value"
                >{{ percent(s.host.cpuUsedPercent) }} / {{ percent(s.host.cpuFreePercent) }}</strong
              >
            </div>
            <div class="stat">
              <span class="label">記憶體可用</span>
              <strong class="value">{{ bytes(s.host.memoryAvailableBytes) }}</strong>
              <span class="muted small"
                >共 {{ bytes(s.host.memoryTotalBytes) }} ·
                {{ percent(s.host.memoryAvailablePercent) }}</span
              >
            </div>
            @for (fs of s.filesystems; track fs.id) {
              <div class="stat">
                <span class="label">磁碟 {{ fs.id }}（{{ fs.stores.join('、') }}）</span>
                <strong class="value">{{ bytes(fs.availableBytes) }}</strong>
                <span class="muted small"
                  >可用 · 共 {{ bytes(fs.totalBytes) }} · {{ percent(fs.availablePercent) }}</span
                >
              </div>
            }
          </div>
          <ul class="plain small">
            @for (store of s.stores; track store.name) {
              <li>
                Store {{ store.name }}：{{ statusLabel(store.status) }}
                @if (store.message) {
                  — {{ store.message }}（這部分一律保護，不會回收）
                }
              </li>
            }
          </ul>
          @if (s.pressure.length > 0) {
            <ul class="plain small">
              @for (p of s.pressure; track p.key) {
                <li>
                  {{ pressureLabel(p.key) }}：{{ p.below ? '低於門檻' : '正常' }}
                  @if (p.belowSince) {
                    （自 {{ p.belowSince | date: 'HH:mm' }}）
                  }
                  @if (p.triggered) {
                    · 已觸發緊急回收
                  }
                </li>
              }
            </ul>
          }
        </article>

        <article class="card stack">
          <h2>Containers</h2>
          <p class="muted small">
            只列出 Ymir 的 container。只有停止、帶回收標記且 workspace 外置的 Agent
            執行環境可能被回收；執行中的工作不會被停止。
          </p>
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Store</th>
                  <th>名稱</th>
                  <th>狀態</th>
                  <th>健康</th>
                  <th class="num">CPU</th>
                  <th class="num">記憶體</th>
                  <th>停止起算</th>
                  <th>回收資格</th>
                </tr>
              </thead>
              <tbody>
                @for (c of s.containers; track c.store + c.shortId) {
                  <tr>
                    <td>{{ c.store }}</td>
                    <td>
                      {{ c.name }}
                      <span class="muted small">{{ c.isAgentRuntime ? '執行環境' : '服務' }}</span>
                    </td>
                    <td>{{ c.state }}</td>
                    <td>{{ c.health ?? '—' }}</td>
                    <td class="num">{{ percent(c.cpuPercent) }}</td>
                    <td class="num">{{ bytes(c.memoryBytes) }}</td>
                    <td>{{ c.unusedSince ? (c.unusedSince | date: 'yyyy-MM-dd HH:mm') : '—' }}</td>
                    <td [class.ok]="eligible(c)">{{ eligibility(c) }}</td>
                  </tr>
                } @empty {
                  <tr>
                    <td colspan="8" class="muted">沒有 Ymir 的 container。</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </article>

        <article class="card stack">
          <h2>Images</h2>
          <p class="muted small">
            大小含與其他 image 共用的 layer（標示
            *），不等於刪除後實際釋放的空間；回收紀錄只記錄檔案系統實測的變化。
          </p>
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Store</th>
                  <th>Tag</th>
                  <th>ID</th>
                  <th class="num">大小</th>
                  <th class="num">引用</th>
                  <th>建立</th>
                  <th>未使用起算</th>
                  <th>回收資格</th>
                </tr>
              </thead>
              <tbody>
                @for (i of s.images; track i.store + i.shortId) {
                  <tr>
                    <td>{{ i.store }}</td>
                    <td>{{ i.tags.length > 0 ? i.tags.join(', ') : '（無 tag）' }}</td>
                    <td class="mono">{{ i.shortId }}</td>
                    <td class="num">{{ bytes(i.sizeBytes) }}{{ i.hasSharedLayers ? ' *' : '' }}</td>
                    <td class="num">{{ i.containerReferences }}</td>
                    <td>{{ i.created | date: 'yyyy-MM-dd' }}</td>
                    <td>{{ i.unusedSince ? (i.unusedSince | date: 'yyyy-MM-dd HH:mm') : '—' }}</td>
                    <td [class.ok]="eligible(i)">{{ eligibility(i) }}</td>
                  </tr>
                } @empty {
                  <tr>
                    <td colspan="8" class="muted">沒有 Ymir 的 image。</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </article>
      }

      <article class="card stack">
        <h2>自動回收政策</h2>
        @if (policy(); as p) {
          <p class="muted small">{{ summary() }}</p>
          @if (snapshot(); as s) {
            <p class="small">
              {{ stateLabel(s.reclaimState) }}。採樣每 {{ s.timing.sampleIntervalSeconds }} 秒，
              不足持續 {{ s.timing.sustainMinutes }} 分鐘才觸發，每輪間隔
              {{ s.timing.cooldownMinutes }} 分鐘，回復門檻 +{{
                s.timing.recoveryMarginPercent
              }}%，每輪最多刪除 {{ s.timing.maxDeletionsPerRound }} 項，每個 repository 至少保留
              {{ s.timing.keepRecentVersions }} 個版本（部署設定）。
            </p>
          }
          <form class="stack" (ngSubmit)="save()">
            <label class="check">
              <input
                type="checkbox"
                name="enabled"
                [ngModel]="draft().enabled"
                (ngModelChange)="patch({ enabled: $event })"
              />
              開啟自動回收（預設關閉）
            </label>
            <div class="grid">
              <label
                >停止的執行環境保留天數
                <input
                  name="containerRetentionDays"
                  type="number"
                  min="0"
                  max="3650"
                  [ngModel]="draft().containerRetentionDays"
                  (ngModelChange)="patch({ containerRetentionDays: $event + '' })"
                />
                <span class="muted small"
                  >從持續停止起算；0 代表不依期限回收（不是立即刪除）。</span
                >
              </label>
              <label
                >未使用 image 保留天數
                <input
                  name="imageRetentionDays"
                  type="number"
                  min="0"
                  max="3650"
                  [ngModel]="draft().imageRetentionDays"
                  (ngModelChange)="patch({ imageRetentionDays: $event + '' })"
                />
                <span class="muted small">從沒有任何 container 引用起算；0 代表不依期限回收。</span>
              </label>
              <label
                >CPU 剩餘比例門檻（%）
                <input
                  name="cpuMinFreePercent"
                  type="number"
                  min="0"
                  max="99"
                  step="0.1"
                  [ngModel]="draft().cpuMinFreePercent"
                  (ngModelChange)="patch({ cpuMinFreePercent: $event + '' })"
                />
              </label>
              <label
                >記憶體可用門檻（GB）
                <input
                  name="memoryMinAvailableGb"
                  type="number"
                  min="0"
                  step="0.1"
                  [ngModel]="draft().memoryMinAvailableGb"
                  (ngModelChange)="patch({ memoryMinAvailableGb: $event + '' })"
                />
              </label>
              <label
                >記憶體可用比例門檻（%）
                <input
                  name="memoryMinAvailablePercent"
                  type="number"
                  min="0"
                  max="99"
                  step="0.1"
                  [ngModel]="draft().memoryMinAvailablePercent"
                  (ngModelChange)="patch({ memoryMinAvailablePercent: $event + '' })"
                />
              </label>
              <label
                >磁碟可用門檻（GB）
                <input
                  name="diskMinAvailableGb"
                  type="number"
                  min="0"
                  step="0.1"
                  [ngModel]="draft().diskMinAvailableGb"
                  (ngModelChange)="patch({ diskMinAvailableGb: $event + '' })"
                />
              </label>
              <label
                >磁碟可用比例門檻（%）
                <input
                  name="diskMinAvailablePercent"
                  type="number"
                  min="0"
                  max="99"
                  step="0.1"
                  [ngModel]="draft().diskMinAvailablePercent"
                  (ngModelChange)="patch({ diskMinAvailablePercent: $event + '' })"
                />
              </label>
            </div>
            <p class="muted small">
              門檻為 0
              代表停用。資源持續不足時只回收上表標示「可回收」的項目，可略過保留天數，但不會略過任何保護。停止的
              container 與 image 通常不占用
              CPU／記憶體，回收不保證降低負載；沒有安全候選時只會留下紀錄。
            </p>
            <div class="row">
              <button type="submit" [disabled]="busy() || !!problem()">儲存</button>
              <button type="button" class="secondary" [disabled]="busy()" (click)="dryRun()">
                Dry run（不刪除）
              </button>
              @if (problem(); as message) {
                <span class="muted small">{{ message }}</span>
              }
              @if (p.updatedAt) {
                <span class="muted small"
                  >{{ p.updatedAt | date: 'yyyy-MM-dd HH:mm' }}
                  {{ p.updatedByName ? '由 ' + p.updatedByName + ' 更新' : '' }}</span
                >
              }
            </div>
          </form>
        } @else if (loading()) {
          <p class="muted">載入中…</p>
        }
        @if (message()) {
          <p class="muted" role="status">{{ message() }}</p>
        }
        @if (dry(); as d) {
          @if (d.result; as result) {
            <div class="stack">
              <h3>Dry run 結果（{{ result.generatedAt | date: 'HH:mm:ss' }}）</h3>
              @for (note of result.notes; track note) {
                <p class="muted small">{{ note }}</p>
              }
              <p class="small">依保留天數會回收 {{ result.retentionCandidates.length }} 項：</p>
              <ul class="plain small">
                @for (
                  item of result.retentionCandidates;
                  track item.store + item.kind + item.name
                ) {
                  <li>{{ item.store }} · {{ item.kind }} · {{ item.name }}</li>
                } @empty {
                  <li class="muted">（無）</li>
                }
              </ul>
              <p class="small">
                資源不足時可考慮的安全候選 {{ result.emergencyCandidates.length }} 項：
              </p>
              <ul class="plain small">
                @for (
                  item of result.emergencyCandidates;
                  track item.store + item.kind + item.name
                ) {
                  <li>{{ item.store }} · {{ item.kind }} · {{ item.name }}</li>
                } @empty {
                  <li class="muted">（無）</li>
                }
              </ul>
            </div>
          } @else if (d.supported) {
            <p class="error">Dry run 失敗：無法連線到主機維護服務。</p>
          }
        }
      </article>

      @if (runs(); as r) {
        @if (r.supported) {
          <article class="card stack">
            <h2>回收紀錄</h2>
            <div class="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>時間</th>
                    <th>觸發</th>
                    <th class="num">候選</th>
                    <th class="num">保護</th>
                    <th class="num">刪除</th>
                    <th class="num">失敗</th>
                    <th class="num">實測釋放</th>
                    <th>摘要</th>
                  </tr>
                </thead>
                <tbody>
                  @for (run of r.runs; track run.id) {
                    <tr>
                      <td>{{ run.startedAt | date: 'yyyy-MM-dd HH:mm' }}</td>
                      <td>{{ trigger(run.trigger) }}</td>
                      <td class="num">{{ run.candidates }}</td>
                      <td class="num">{{ run.protected }}</td>
                      <td class="num">{{ run.deleted }}</td>
                      <td class="num">{{ run.failed }}</td>
                      <td class="num">{{ bytes(freed(run.filesystems)) }}</td>
                      <td class="wrap">
                        {{ run.summary }}
                        @if (run.items.length > 0) {
                          <details>
                            <summary>項目（{{ run.items.length }}）</summary>
                            <ul class="plain">
                              @for (item of run.items; track $index) {
                                <li>
                                  {{ item.store }} · {{ item.kind }} · {{ item.name }} ·
                                  {{ item.result }}{{ item.reason ? '：' + item.reason : '' }}
                                </li>
                              }
                            </ul>
                          </details>
                        }
                      </td>
                    </tr>
                  } @empty {
                    <tr>
                      <td colspan="8" class="muted">尚無回收紀錄。</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </article>
        }
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
      max-width: 78rem;
      margin: 0 auto;
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }
    .head,
    .row {
      display: flex;
      flex-wrap: wrap;
      gap: 0.75rem;
      align-items: center;
      justify-content: space-between;
    }
    .row {
      justify-content: flex-start;
    }
    h1,
    h2,
    h3,
    p {
      margin: 0;
    }
    h2 {
      font-size: 1.125rem;
    }
    h3 {
      font-size: 1rem;
    }
    .small {
      font-size: 0.8125rem;
    }
    .stats {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 13rem), 1fr));
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
      font-size: 1.375rem;
      line-height: 1.2;
    }
    .plain {
      margin: 0;
      padding-left: 1.125rem;
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 16rem), 1fr));
      gap: 0.875rem 1rem;
    }
    label {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    label.check {
      flex-direction: row;
      align-items: center;
      gap: 0.5rem;
    }
    .table-wrap {
      overflow-x: auto;
    }
    table {
      width: 100%;
      border-collapse: collapse;
      font-size: 0.875rem;
    }
    th,
    td {
      text-align: left;
      padding: 0.5rem 0.625rem;
      border-bottom: 1px solid var(--border);
      white-space: nowrap;
      vertical-align: top;
    }
    th {
      color: var(--text-muted);
      font-weight: 600;
      font-size: 0.8125rem;
    }
    td.wrap {
      white-space: normal;
      min-width: 16rem;
    }
    .num {
      text-align: right;
    }
    .mono {
      font-family: ui-monospace, monospace;
    }
    .ok {
      color: var(--accent);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResourcesPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly resources = signal<AdminResources | null>(null);
  protected readonly runs = signal<AdminReclaimRuns | null>(null);
  protected readonly policy = signal<ReclamationPolicyResponse | null>(null);
  protected readonly dry = signal<AdminDryRun | null>(null);
  protected readonly draft = signal<ReclamationDraft>(
    reclamationDraftFrom({
      enabled: false,
      containerRetentionDays: 0,
      imageRetentionDays: 0,
      cpuMinFreePercent: 0,
      memoryMinAvailableGb: 0,
      memoryMinAvailablePercent: 0,
      diskMinAvailableGb: 0,
      diskMinAvailablePercent: 0,
    }),
  );
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);

  protected readonly snapshot = computed(() => this.resources()?.snapshot ?? null);
  protected readonly problem = computed(() => reclamationDraftProblem(this.draft()));
  protected readonly summary = computed(() => {
    const p = this.policy();
    return p ? reclamationSummary(p.policy) : '';
  });

  protected readonly bytes = formatBytes;
  protected readonly percent = formatPercent;
  protected readonly eligibility = eligibilityLabel;
  protected readonly eligible = isEligible;
  protected readonly statusLabel = dataStatusLabel;
  protected readonly stateLabel = reclaimStateLabel;
  protected readonly trigger = triggerLabel;
  protected readonly pressureLabel = pressureKeyLabel;
  protected readonly freed = freedBytes;

  ngOnInit(): void {
    this.api.adminGetReclamationPolicy().subscribe({
      next: (policy) => this.applyPolicy(policy),
      error: (e: unknown) => this.error.set(describeApiError(e)),
    });
    this.refresh();
    // 服務每 30 秒採樣一次；頁面開著時跟著更新。
    const timer = setInterval(() => this.refresh(), 30_000);
    this.destroyRef.onDestroy(() => clearInterval(timer));
  }

  protected refresh(): void {
    this.loading.set(true);
    this.api.adminGetResources().subscribe({
      next: (resources) => {
        this.resources.set(resources);
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.loading.set(false);
      },
    });
    this.api.adminGetReclaimRuns().subscribe({
      next: (runs) => this.runs.set(runs),
      error: (e: unknown) => this.error.set(describeApiError(e)),
    });
  }

  protected patch(change: Partial<ReclamationDraft>): void {
    this.draft.update((d) => ({ ...d, ...change }));
  }

  protected save(): void {
    if (this.problem()) return;
    this.busy.set(true);
    this.message.set(null);
    this.api.adminSaveReclamationPolicy(toReclamationPolicy(this.draft())).subscribe({
      next: (policy) => {
        this.applyPolicy(policy);
        this.busy.set(false);
        // 自動回收狀態（開啟 / 鎖不可用）來自服務的快照：重新讀取。
        this.refresh();
        this.message.set(
          policy.synced === false
            ? '已儲存；目前無法同步到主機維護服務，會在一分鐘內自動重試。'
            : '已儲存。',
        );
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.busy.set(false);
      },
    });
  }

  protected dryRun(): void {
    this.busy.set(true);
    this.api.adminReclaimDryRun().subscribe({
      next: (result) => {
        this.dry.set(result);
        this.busy.set(false);
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.busy.set(false);
      },
    });
  }

  private applyPolicy(policy: ReclamationPolicyResponse): void {
    this.policy.set(policy);
    this.draft.set(reclamationDraftFrom(policy.policy));
    this.loading.set(false);
  }
}
