import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subscription, timer } from 'rxjs';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { ConversationOneDrive } from '../../core/api/api-types';
import { onedriveFolderLabel, onedriveSyncSummary } from '../../core/connectors/onedrive-rules';

/** 同步中每 2 秒重新讀取狀態，最多約 2 分鐘（之後由「重新整理」或下一次執行更新）。 */
const POLL_INTERVAL_MS = 2000;
const MAX_POLLS = 60;

/**
 * 檔案面板的「雲端保存狀態」（ADR-0013 §4）：與 Agent 任務結果分開顯示，
 * 同步失敗不代表任務失敗；可手動同步 / 重試。管理員沒有開放 OneDrive 時不顯示。
 */
@Component({
  selector: 'app-onedrive-sync',
  imports: [RouterLink],
  providers: [DatePipe],
  template: `
    @if (summary(); as s) {
      <section class="cloud" [class]="s.tone" aria-label="雲端保存狀態" aria-live="polite">
        <p>
          <span class="icon" aria-hidden="true">☁</span>
          {{ s.text }}
        </p>
        @if (folder(); as f) {
          <p class="muted small" [title]="f">OneDrive：{{ f }}</p>
        }
        @if (error(); as e) {
          <p class="error small">{{ e }}</p>
        }
        <div class="actions">
          @if (s.needsSettings) {
            <a class="link small" routerLink="/settings">前往設定</a>
          }
          @if (s.canSync) {
            <button type="button" class="link small" [disabled]="busy()" (click)="syncNow()">
              {{ s.tone === 'error' ? '重試' : '立即同步' }}
            </button>
          }
        </div>
      </section>
    }
  `,
  styles: `
    .cloud {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      padding: 0.5rem 0.75rem;
      border-radius: 0.5rem;
      border: 1px solid var(--border);
      background: var(--surface-muted);
      font-size: 0.8125rem;
    }
    .cloud.error {
      border-color: var(--danger, #c62828);
    }
    p {
      margin: 0;
      overflow-wrap: anywhere;
    }
    .small {
      font-size: 0.75rem;
    }
    .actions {
      display: flex;
      gap: 0.75rem;
    }
    .actions:empty {
      display: none;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OneDriveSync {
  private readonly api = inject(ApiService);
  private readonly datePipe = inject(DatePipe);

  readonly conversationId = input.required<string>();
  /** 檔案面板重新載入中；結束時（執行完成、按重新整理）一併更新雲端狀態。 */
  readonly reloading = input(false);

  private readonly status = signal<ConversationOneDrive | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly summary = computed(() =>
    onedriveSyncSummary(this.status(), (iso) => this.datePipe.transform(iso, 'M/d HH:mm') ?? iso),
  );
  protected readonly folder = computed(() => onedriveFolderLabel(this.status()));

  private request: Subscription | null = null;
  private poll: Subscription | null = null;
  private polls = 0;

  constructor() {
    effect(() => {
      this.conversationId();
      if (!this.reloading()) {
        untracked(() => {
          this.polls = 0;
          this.load();
        });
      }
    });
    inject(DestroyRef).onDestroy(() => {
      this.request?.unsubscribe();
      this.poll?.unsubscribe();
    });
  }

  protected syncNow(): void {
    this.busy.set(true);
    this.error.set(null);
    this.request?.unsubscribe();
    this.request = this.api.syncConversationOneDrive(this.conversationId()).subscribe({
      next: (status) => {
        this.busy.set(false);
        this.polls = 0;
        this.apply(status);
      },
      error: (e: unknown) => {
        this.busy.set(false);
        this.error.set(describeApiError(e));
      },
    });
  }

  private load(): void {
    this.request?.unsubscribe();
    this.request = this.api.getConversationOneDrive(this.conversationId()).subscribe({
      next: (status) => this.apply(status),
      // 雲端狀態只是輔助資訊：讀不到時不顯示，不干擾檔案清單。
      error: () => this.status.set(null),
    });
  }

  private apply(status: ConversationOneDrive): void {
    this.status.set(status);
    this.poll?.unsubscribe();
    this.poll = null;
    if (status.state === 'Pending' && this.polls < MAX_POLLS) {
      this.polls++;
      this.poll = timer(POLL_INTERVAL_MS).subscribe(() => this.load());
    }
  }
}
