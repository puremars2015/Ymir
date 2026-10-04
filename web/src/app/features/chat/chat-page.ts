import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  input,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { ChatMessage, Conversation } from '../../core/api/api-types';
import {
  applyExecutionEvent,
  ExecutionView,
  initialExecutionView,
} from '../../core/executions/execution-state';
import { ExecutionStreamService } from '../../core/executions/execution-stream.service';

interface LiveTurn {
  prompt: string;
  executionId: string;
  view: ExecutionView;
}

/**
 * 對話頁（SA §5）：載入歷史訊息、送出訊息、以 SSE 顯示 Agent 即時回應、停止執行。
 * 執行結束後重新載入歷史，畫面以後端保存的訊息為準。
 */
@Component({
  selector: 'app-chat-page',
  imports: [FormsModule, RouterLink],
  templateUrl: './chat-page.html',
  styleUrl: './chat-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChatPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly streams = inject(ExecutionStreamService);
  private readonly destroyRef = inject(DestroyRef);

  readonly conversationId = input.required<string>();
  protected readonly conversation = signal<Conversation | null>(null);
  protected readonly messages = signal<ChatMessage[]>([]);
  protected readonly live = signal<LiveTurn | null>(null);
  protected readonly prompt = signal('');
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.api.getConversation(this.conversationId()).subscribe({
      next: (conversation) => this.conversation.set(conversation),
      error: (error: unknown) => this.error.set(describeApiError(error)),
    });
    this.reloadMessages();
  }

  protected send(): void {
    const prompt = this.prompt().trim();
    if (!prompt || this.live()) {
      return;
    }

    this.error.set(null);
    this.api.sendMessage(this.conversationId(), prompt).subscribe({
      next: (accepted) => {
        this.prompt.set('');
        this.live.set({ prompt, executionId: accepted.executionId, view: initialExecutionView() });
        this.streams
          .stream(accepted.eventStreamUrl)
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            next: (event) =>
              this.live.update((turn) =>
                turn ? { ...turn, view: applyExecutionEvent(turn.view, event) } : turn,
              ),
            error: (error: unknown) => this.finish(describeApiError(error)),
            complete: () => this.finish(null),
          });
      },
      error: (error: unknown) => this.error.set(describeApiError(error)),
    });
  }

  /** 送出取消；Agent 中止後串流會收到 execution.cancelled 並結束。 */
  protected stop(): void {
    const turn = this.live();
    if (turn) {
      this.api
        .cancelExecution(turn.executionId)
        .subscribe({ error: (error: unknown) => this.error.set(describeApiError(error)) });
    }
  }

  private finish(error: string | null): void {
    this.error.set(error);
    this.reloadMessages(() => this.live.set(null));
  }

  private reloadMessages(after?: () => void): void {
    this.api.listMessages(this.conversationId()).subscribe({
      next: (messages) => {
        this.messages.set(messages);
        after?.();
      },
      error: (error: unknown) => this.error.set(describeApiError(error)),
    });
  }
}
