import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ExecutionStreamService } from '../../core/executions/execution-stream.service';
import {
  applyExecutionEvent,
  ExecutionView,
  initialExecutionView,
} from '../../core/executions/execution-state';

interface ChatTurn {
  prompt: string;
  view: ExecutionView;
}

/**
 * Sprint 0 開發用聊天頁：呼叫 `/api/dev/agent-stream`，驗證 SSE 事件契約與畫面呈現。
 * 正式的 Conversation / Workspace 流程在 Sprint 1、3 實作。
 */
@Component({
  selector: 'app-dev-chat',
  imports: [FormsModule],
  templateUrl: './dev-chat.html',
  styleUrl: './dev-chat.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DevChat {
  private readonly streams = inject(ExecutionStreamService);
  private readonly destroyRef = inject(DestroyRef);
  private current?: Subscription;

  protected readonly prompt = signal('');
  protected readonly turns = signal<ChatTurn[]>([]);
  protected readonly running = signal(false);

  protected send(): void {
    const prompt = this.prompt().trim();
    if (!prompt || this.running()) {
      return;
    }

    this.prompt.set('');
    this.running.set(true);
    this.turns.update((turns) => [...turns, { prompt, view: initialExecutionView() }]);

    const url = `/api/dev/agent-stream?prompt=${encodeURIComponent(prompt)}`;
    this.current = this.streams
      .stream(url)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (event) => this.updateLastTurn((view) => applyExecutionEvent(view, event)),
        error: (error: Error) => {
          this.updateLastTurn((view) => ({
            ...view,
            status: 'failed',
            error: { code: 'STREAM_ERROR', message: error.message },
          }));
          this.running.set(false);
        },
        complete: () => this.running.set(false),
      });
  }

  /** 關閉 SSE 連線；開發端點把 execution 綁在 request 上，斷線即中止 Agent。 */
  protected stop(): void {
    this.current?.unsubscribe();
    this.updateLastTurn((view) => ({ ...view, status: 'cancelled', statusText: null }));
    this.running.set(false);
  }

  private updateLastTurn(update: (view: ExecutionView) => ExecutionView): void {
    this.turns.update((turns) =>
      turns.map((turn, index) =>
        index === turns.length - 1 ? { ...turn, view: update(turn.view) } : turn,
      ),
    );
  }
}
