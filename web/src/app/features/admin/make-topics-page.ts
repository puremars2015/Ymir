import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { forkJoin, Observable } from 'rxjs';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { AdminMakeTopic } from '../../core/api/api-types';
import {
  draftFromTopic,
  emptyMakeTopicDraft,
  MAKE_TOPIC_DESCRIPTION_MAX,
  MAKE_TOPIC_INSTRUCTIONS_MAX,
  MAKE_TOPIC_NAME_MAX,
  MakeTopicDraft,
  makeTopicDraftProblem,
  moveTopic,
  nextSortOrder,
  sortOrderOf,
  sortTopics,
  toSaveRequest,
} from '../../core/make/make-topic-rules';
import { MakeTopicStore } from '../../core/make/make-topic.store';
import { AdminTabs } from './admin-tabs';

/**
 * Make 主題管理（Admin）：對話輸入 `/make` 時顯示的主題按鈕。
 * 建置指示只在後端組合給 Agent，一般使用者看不到。
 */
@Component({
  selector: 'app-make-topics-page',
  imports: [FormsModule, AdminTabs],
  template: `
    <section class="admin">
      <app-admin-tabs />
      <header class="head">
        <h1>Make 主題</h1>
        @if (editing() === null) {
          <button type="button" (click)="startCreate()">新增主題</button>
        }
      </header>
      <p class="muted hint">
        使用者在對話輸入 <code>/make</code> 送出時，會看到啟用中的主題按鈕；點了按鈕，Agent
        會依「建置指示」先問需求。輸入 <code>/make 描述</code> 時，Agent
        會從啟用中的主題判斷最適合的一個。
      </p>

      @if (editing() !== null) {
        <form class="card stack editor" (ngSubmit)="save()">
          <h2>{{ editing() === 'new' ? '新增主題' : '編輯主題' }}</h2>
          <label
            >名稱（按鈕文字）
            <input
              name="name"
              [maxlength]="nameMax"
              [ngModel]="draft().name"
              (ngModelChange)="patch({ name: $event })"
              required
          /></label>
          <label
            >說明（顯示在按鈕上，選填）
            <input
              name="description"
              [maxlength]="descriptionMax"
              [ngModel]="draft().description"
              (ngModelChange)="patch({ description: $event })"
          /></label>
          <label
            >給 Agent 的建置指示
            <textarea
              name="instructions"
              rows="6"
              [maxlength]="instructionsMax"
              [ngModel]="draft().instructions"
              (ngModelChange)="patch({ instructions: $event })"
              required
            ></textarea>
            <span class="muted small counter"
              >{{ draft().instructions.length }} / {{ instructionsMax }}</span
            >
          </label>
          <label class="check">
            <input
              type="checkbox"
              name="enabled"
              [ngModel]="draft().isEnabled"
              (ngModelChange)="patch({ isEnabled: $event })"
            />
            啟用（顯示給使用者）
          </label>
          <div class="row">
            <button type="submit" [disabled]="busy() || !!problem()">儲存</button>
            <button type="button" class="secondary" (click)="cancelEdit()">取消</button>
            @if (problem(); as p) {
              <span class="muted small">{{ p }}</span>
            }
          </div>
        </form>
      }

      @if (message()) {
        <p class="muted" role="status">{{ message() }}</p>
      }
      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }

      <ul class="topic-list">
        @for (topic of sorted(); track topic.id; let i = $index, first = $first, last = $last) {
          <li class="card topic" [class.disabled]="!topic.isEnabled">
            <div class="info">
              <strong>{{ topic.name }}</strong>
              @if (!topic.isEnabled) {
                <span class="badge">已停用</span>
              }
              @if (topic.description) {
                <p class="muted small">{{ topic.description }}</p>
              }
              <p class="instructions small">{{ topic.instructions }}</p>
            </div>
            <div class="actions">
              <button
                type="button"
                class="link"
                aria-label="上移"
                title="上移"
                [disabled]="busy() || first"
                (click)="move(i, -1)"
              >
                ↑
              </button>
              <button
                type="button"
                class="link"
                aria-label="下移"
                title="下移"
                [disabled]="busy() || last"
                (click)="move(i, 1)"
              >
                ↓
              </button>
              <button type="button" class="link" [disabled]="busy()" (click)="startEdit(topic)">
                編輯
              </button>
              <button type="button" class="link" [disabled]="busy()" (click)="toggle(topic)">
                {{ topic.isEnabled ? '停用' : '啟用' }}
              </button>
              <button
                type="button"
                class="link danger-text"
                [disabled]="busy()"
                (click)="remove(topic)"
              >
                刪除
              </button>
            </div>
          </li>
        } @empty {
          <li class="muted">
            {{ loading() ? '載入中…' : '還沒有主題；使用者仍可用「/make 描述」請 Agent 建置。' }}
          </li>
        }
      </ul>
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
      gap: 1rem;
    }
    h1,
    h2,
    p {
      margin: 0;
    }
    h2 {
      font-size: 1.125rem;
    }
    .hint,
    .small {
      font-size: 0.8125rem;
    }
    .editor label {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    .editor label.check {
      flex-direction: row;
      align-items: center;
      gap: 0.5rem;
    }
    .counter {
      align-self: flex-end;
    }
    .row {
      display: flex;
      align-items: center;
      gap: 0.75rem;
    }
    .topic-list {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }
    .topic {
      display: flex;
      gap: 1rem;
      justify-content: space-between;
      align-items: flex-start;
    }
    .topic.disabled .info {
      opacity: 0.6;
    }
    .info {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      min-width: 0;
    }
    .instructions {
      white-space: pre-wrap;
      color: var(--text-muted);
      display: -webkit-box;
      -webkit-line-clamp: 3;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }
    .badge {
      align-self: flex-start;
      font-size: 0.75rem;
      padding: 0 0.5rem;
      border-radius: 999px;
      border: 1px solid var(--border);
      color: var(--text-muted);
    }
    .actions {
      display: inline-flex;
      gap: 0.75rem;
      white-space: nowrap;
    }
    @media (max-width: 40rem) {
      .topic {
        flex-direction: column;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MakeTopicsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly composerTopics = inject(MakeTopicStore);

  protected readonly nameMax = MAKE_TOPIC_NAME_MAX;
  protected readonly descriptionMax = MAKE_TOPIC_DESCRIPTION_MAX;
  protected readonly instructionsMax = MAKE_TOPIC_INSTRUCTIONS_MAX;

  protected readonly topics = signal<AdminMakeTopic[]>([]);
  protected readonly sorted = computed(() => sortTopics(this.topics()));
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);

  /** null：沒有在編輯；'new'：新增；否則為編輯中的主題。 */
  protected readonly editing = signal<AdminMakeTopic | 'new' | null>(null);
  protected readonly draft = signal<MakeTopicDraft>(emptyMakeTopicDraft());
  protected readonly problem = computed(() => makeTopicDraftProblem(this.draft()));

  ngOnInit(): void {
    this.api.adminListMakeTopics().subscribe({
      next: (topics) => {
        this.topics.set(topics);
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.loading.set(false);
      },
    });
  }

  protected patch(change: Partial<MakeTopicDraft>): void {
    this.draft.update((d) => ({ ...d, ...change }));
  }

  protected startCreate(): void {
    this.draft.set(emptyMakeTopicDraft());
    this.editing.set('new');
  }

  protected startEdit(topic: AdminMakeTopic): void {
    this.draft.set(draftFromTopic(topic));
    this.editing.set(topic);
  }

  protected cancelEdit(): void {
    this.editing.set(null);
  }

  protected save(): void {
    const editing = this.editing();
    if (editing === null || this.problem()) {
      return;
    }
    if (editing === 'new') {
      const request = toSaveRequest(this.draft(), nextSortOrder(this.topics()));
      this.run(this.api.adminCreateMakeTopic(request), (created) => {
        this.topics.update((list) => [...list, created]);
        this.editing.set(null);
        this.message.set(`已新增主題「${created.name}」。`);
      });
    } else {
      const request = toSaveRequest(this.draft(), sortOrderOf(editing));
      this.run(this.api.adminUpdateMakeTopic(editing.id, request), (updated) => {
        this.replace([updated]);
        this.editing.set(null);
        this.message.set(`已更新主題「${updated.name}」。`);
      });
    }
  }

  protected toggle(topic: AdminMakeTopic): void {
    const request = toSaveRequest(
      { ...draftFromTopic(topic), isEnabled: !topic.isEnabled },
      sortOrderOf(topic),
    );
    this.run(this.api.adminUpdateMakeTopic(topic.id, request), (updated) => {
      this.replace([updated]);
      this.message.set(`主題「${updated.name}」已${updated.isEnabled ? '啟用' : '停用'}。`);
    });
  }

  protected move(index: number, direction: -1 | 1): void {
    const changes = moveTopic(this.topics(), index, direction);
    if (changes.length === 0) {
      return;
    }
    const byId = new Map(this.topics().map((t) => [t.id, t]));
    const requests = changes.flatMap(({ id, sortOrder }) => {
      const topic = byId.get(id);
      return topic
        ? [this.api.adminUpdateMakeTopic(id, toSaveRequest(draftFromTopic(topic), sortOrder))]
        : [];
    });
    this.run(forkJoin(requests), (updated) => this.replace(updated));
  }

  protected remove(topic: AdminMakeTopic): void {
    if (!confirm(`確定刪除主題「${topic.name}」？使用者將不再看到這個按鈕。`)) {
      return;
    }
    this.run(this.api.adminDeleteMakeTopic(topic.id), () => {
      this.topics.update((list) => list.filter((t) => t.id !== topic.id));
      const editing = this.editing();
      if (editing !== null && editing !== 'new' && editing.id === topic.id) {
        this.editing.set(null);
      }
      this.message.set(`已刪除主題「${topic.name}」。`);
    });
  }

  private replace(updated: AdminMakeTopic[]): void {
    const byId = new Map(updated.map((t) => [t.id, t]));
    this.topics.update((list) => list.map((t) => byId.get(t.id) ?? t));
  }

  private run<T>(request: Observable<T>, done: (value: T) => void): void {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    request.subscribe({
      next: (value) => {
        done(value);
        this.busy.set(false);
        // 對話輸入框的主題清單（root 快取）跟著更新
        this.composerTopics.refresh();
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.busy.set(false);
      },
    });
  }
}
