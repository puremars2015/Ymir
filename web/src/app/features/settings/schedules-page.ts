import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { Conversation, ScheduledTask, SaveScheduledTask } from '../../core/api/api-types';
import { ModelStore } from '../../core/models/model.store';
import {
  effectiveThinking,
  effortLabels,
  thinkingLevels,
} from '../../core/models/thinking-options';

@Component({
  selector: 'app-schedules-page',
  imports: [FormsModule, DatePipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>排程與提醒</h2>
    <p class="muted">
      提醒只送通知；Agent 工作會使用所選模型，計入你的配額與預算。時間依 {{ zone }} 計算。
    </p>
    @if (error()) {
      <p role="alert">{{ error() }}</p>
    }
    @if (notice()) {
      <p role="status">{{ notice() }}</p>
    }
    <form class="stack" (ngSubmit)="save()">
      <h3>{{ editing() ? '編輯排程' : '新增排程' }}</h3>
      <label
        >對話
        <select name="conversation" [(ngModel)]="conversationId" required [disabled]="!!editing()">
          <option value="">選擇對話</option>
          @for (c of conversations(); track c.id) {
            <option [value]="c.id">{{ c.title }}</option>
          }
        </select>
      </label>
      <label
        >種類
        <select name="kind" [(ngModel)]="kind">
          <option value="Reminder">提醒事項（不啟動 Agent）</option>
          <option value="AgentRun">執行 Agent 工作</option>
        </select>
      </label>
      <label
        >內容<textarea
          name="prompt"
          [(ngModel)]="prompt"
          required
          maxlength="32000"
          rows="3"
        ></textarea>
      </label>
      @if (kind === 'AgentRun') {
        <label
          >模型<select name="model" [(ngModel)]="modelId" required>
            <option value="">選擇模型</option>
            @for (m of models.models(); track m.id) {
              <option [value]="m.id">{{ m.displayName }}</option>
            }
          </select></label
        >
      }
      @if (kind === 'AgentRun' && levels().length > 0) {
        <label
          >思考深度
          <select name="thinking" [(ngModel)]="thinkingLevel">
            <option [ngValue]="null">模型預設</option>
            @for (level of levels(); track level) {
              <option [value]="level">{{ effortLabels[level] ?? level }}</option>
            }
          </select>
        </label>
      }
      <label
        >執行方式<select name="repeat" [(ngModel)]="repeat">
          <option [ngValue]="false">一次</option>
          <option [ngValue]="true">重複（Cron）</option>
        </select></label
      >
      @if (repeat) {
        <label
          >Cron（分 時 日 月 星期）<input
            name="cron"
            [(ngModel)]="cron"
            required
            maxlength="100"
            placeholder="0 9 * * 1-5"
        /></label>
        <p class="muted">
          例如 0 9 * * 1-5：工作日早上九點。間隔至少一小時，實際上限依管理員政策。
        </p>
      } @else {
        <label
          >提醒／執行時間<input name="runAt" type="datetime-local" [(ngModel)]="runAt" required
        /></label>
      }
      <div class="actions">
        <button type="submit" [disabled]="busy()">儲存排程</button>
        @if (editing()) {
          <button type="button" (click)="reset()">取消編輯</button>
        }
      </div>
    </form>
    <h3>我的排程</h3>
    @if (loading()) {
      <p role="status">載入中…</p>
    }
    @for (task of tasks(); track task.id) {
      <article class="stack">
        <strong
          >{{ task.kind === 'Reminder' ? '提醒' : 'Agent 工作' }} ·
          {{ statusLabel(task.status) }}</strong
        >
        <p class="content">{{ task.prompt }}</p>
        <a [routerLink]="['/c', task.conversationId]">開啟對話</a>
        <span
          >下次：{{ task.nextRunAt ? (task.nextRunAt | date: 'yyyy-MM-dd HH:mm') : '—' }} ·
          {{ task.timeZone }}</span
        >
        <span
          >最近：{{ task.lastRunAt ? (task.lastRunAt | date: 'yyyy-MM-dd HH:mm') : '—' }}
          {{ task.lastResult }}</span
        >
        @if (task.status !== 'Ended') {
          <div class="actions">
            <button type="button" [disabled]="busy()" (click)="edit(task)">編輯</button>
            <button
              type="button"
              [disabled]="busy()"
              (click)="change(task, task.status === 'Active' ? 'pause' : 'resume')"
            >
              {{ task.status === 'Active' ? '暫停' : '恢復' }}
            </button>
            <button type="button" [disabled]="busy()" (click)="change(task, 'delete')">
              結束排程
            </button>
          </div>
        }
      </article>
    } @empty {
      @if (!loading()) {
        <p>還沒有排程。</p>
      }
    }
  `,
  styles: `
    .stack {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }
    form,
    article {
      border: 1px solid var(--border);
      border-radius: 0.75rem;
      padding: 1rem;
      margin: 1rem 0;
    }
    label {
      display: flex;
      flex-direction: column;
      gap: 0.3rem;
    }
    input,
    select,
    textarea {
      width: 100%;
      box-sizing: border-box;
      padding: 0.6rem;
      color: var(--text);
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: 0.4rem;
    }
    .actions {
      display: flex;
      gap: 0.5rem;
      flex-wrap: wrap;
    }
    button {
      padding: 0.5rem 0.8rem;
      cursor: pointer;
    }
    .content {
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }
  `,
})
export class SchedulesPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  protected readonly models = inject(ModelStore);
  protected readonly tasks = signal<ScheduledTask[]>([]);
  protected readonly conversations = signal<Conversation[]>([]);
  protected readonly editing = signal<ScheduledTask | null>(null);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly busy = signal(false);
  protected readonly loading = signal(true);
  protected zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  protected conversationId = '';
  protected kind: SaveScheduledTask['kind'] = 'Reminder';
  protected prompt = '';
  protected modelId = '';
  protected thinkingLevel: string | null = null;
  protected readonly effortLabels = effortLabels;
  protected levels(): readonly string[] {
    return thinkingLevels(this.models.models().find((m) => m.id === this.modelId));
  }
  protected repeat = false;
  protected cron = '0 9 * * 1-5';
  protected runAt = '';
  ngOnInit(): void {
    this.models.load();
    this.conversationId = this.route.snapshot.queryParamMap.get('conversationId') ?? '';
    this.api.listConversations().subscribe({
      next: (c) => this.conversations.set(c),
      error: (e) => this.error.set(describeApiError(e)),
    });
    this.load();
  }
  private load(): void {
    this.loading.set(true);
    this.api.listScheduledTasks().subscribe({
      next: (t) => {
        this.tasks.set(t);
        this.loading.set(false);
      },
      error: (e) => {
        this.error.set(describeApiError(e));
        this.loading.set(false);
      },
    });
  }
  protected save(): void {
    if (this.busy()) return;
    this.error.set('');
    this.notice.set('');
    const at = new Date(this.runAt);
    if (
      !this.conversationId ||
      !this.prompt.trim() ||
      (!this.repeat && (!Number.isFinite(at.getTime()) || at.getTime() < Date.now() + 60000))
    ) {
      this.error.set('請填寫對話、內容，以及至少一分鐘後的時間。');
      return;
    }
    this.busy.set(true);
    this.api
      .saveScheduledTask(
        {
          conversationId: this.conversationId,
          kind: this.kind,
          prompt: this.prompt,
          modelId: this.kind === 'AgentRun' ? this.modelId : null,
          thinkingLevel: effectiveThinking(
            this.models.models().find((m) => m.id === this.modelId),
            this.thinkingLevel,
          ),
          cronExpression: this.repeat ? this.cron : null,
          timeZone: this.zone,
          runAt: this.repeat ? null : at.toISOString(),
        },
        this.editing(),
      )
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.reset();
          this.notice.set('排程已儲存。');
          this.load();
        },
        error: (e) => {
          this.busy.set(false);
          this.error.set(describeApiError(e));
        },
      });
  }
  protected edit(task: ScheduledTask): void {
    this.editing.set(task);
    this.conversationId = task.conversationId;
    this.kind = task.kind;
    this.prompt = task.prompt;
    this.modelId = task.modelId ?? '';
    this.thinkingLevel = task.thinkingLevel;
    this.repeat = !!task.cronExpression;
    this.cron = task.cronExpression ?? '0 9 * * 1-5';
    this.zone = task.timeZone;
    this.runAt = task.nextRunAt ? localInput(task.nextRunAt) : '';
  }
  protected reset(): void {
    this.editing.set(null);
    this.prompt = '';
    this.runAt = '';
    this.zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  }
  protected statusLabel(status: ScheduledTask['status']): string {
    return { Active: '啟用', Paused: '暫停', Ended: '已結束', PendingConfirmation: '待確認' }[
      status
    ];
  }
  protected change(task: ScheduledTask, action: 'pause' | 'resume' | 'delete'): void {
    this.busy.set(true);
    this.error.set('');
    this.api.changeScheduledTask(task, action).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: (e) => {
        this.busy.set(false);
        this.error.set(describeApiError(e));
        this.load();
      },
    });
  }
}
function localInput(value: string): string {
  const at = new Date(value);
  return new Date(at.getTime() - at.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
}
