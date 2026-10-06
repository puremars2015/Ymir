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
import { ComposerSubmission } from '../../core/make/make-command';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { ChatMessage, Conversation } from '../../core/api/api-types';
import {
  applyExecutionEvent,
  ExecutionView,
  initialExecutionView,
} from '../../core/executions/execution-state';
import { ExecutionStreamService } from '../../core/executions/execution-stream.service';
import { resolveModel } from '../../core/models/model-selection';
import { ModelStore } from '../../core/models/model.store';
import { NavigationStore } from '../../core/navigation/navigation.store';
import { PendingPromptService } from '../../core/navigation/pending-prompt.service';
import { Composer } from '../../shared/composer';
import { AssistantText } from '../../shared/assistant-text';
import { ModelPicker } from '../../shared/model-picker';

interface LiveTurn {
  prompt: string;
  executionId: string;
  view: ExecutionView;
}

/**
 * 對話頁（SA §5）：載入歷史訊息、送出訊息、以 SSE 顯示 Agent 即時回應、停止執行。
 * 執行結束後重新載入歷史，畫面以後端保存的訊息為準。
 * 從側邊欄切換對話時 router 會重用此元件，因此以 conversationId 的變化重新載入。
 */
@Component({
  selector: 'app-chat-page',
  imports: [RouterLink, Composer, ModelPicker, AssistantText],
  templateUrl: './chat-page.html',
  styleUrl: './chat-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChatPage {
  private readonly api = inject(ApiService);
  private readonly streams = inject(ExecutionStreamService);
  private readonly store = inject(NavigationStore);
  private readonly pending = inject(PendingPromptService);
  protected readonly modelStore = inject(ModelStore);

  readonly conversationId = input.required<string>();
  protected readonly conversation = signal<Conversation | null>(null);
  protected readonly messages = signal<ChatMessage[]>([]);
  protected readonly live = signal<LiveTurn | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly project = computed(() => this.store.project(this.conversation()?.projectId));

  /** 這個對話本次選的模型；未選時沿用對話上次的模型 → 個人偏好 → 預設。 */
  private readonly chosenModel = signal<string | null>(null);
  protected readonly selectedModel = computed(
    () =>
      this.chosenModel() ??
      resolveModel(
        this.modelStore.models(),
        this.conversation()?.modelId,
        this.modelStore.preferred(),
      ),
  );

  private stream: Subscription | null = null;

  constructor() {
    this.modelStore.load();
    effect(() => {
      const id = this.conversationId();
      untracked(() => this.load(id));
    });
    inject(DestroyRef).onDestroy(() => this.stream?.unsubscribe());
  }

  protected selectModel(modelId: string): void {
    this.chosenModel.set(modelId);
    this.modelStore.remember(modelId);
  }

  protected submit(submission: ComposerSubmission): void {
    this.send(submission.content, this.selectedModel(), submission.makeTopicId);
  }

  protected send(
    prompt: string,
    modelId: string | null = this.selectedModel(),
    makeTopicId: string | null = null,
  ): void {
    if (this.live()) {
      return;
    }

    const conversationId = this.conversationId();
    this.error.set(null);
    this.api.sendMessage(conversationId, prompt, modelId, makeTopicId).subscribe({
      next: (accepted) => {
        if (conversationId !== this.conversationId()) {
          return; // 送出後使用者已切到別的對話；執行在背景繼續，回來時看歷史即可
        }
        this.store.touch(conversationId);
        this.live.set({ prompt, executionId: accepted.executionId, view: initialExecutionView() });
        this.stream = this.streams.stream(accepted.eventStreamUrl).subscribe({
          next: (event) =>
            this.live.update((turn) =>
              turn ? { ...turn, view: applyExecutionEvent(turn.view, event) } : turn,
            ),
          error: (error: unknown) => this.finish(conversationId, describeApiError(error)),
          complete: () => this.finish(conversationId, null),
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

  private load(conversationId: string): void {
    this.stream?.unsubscribe();
    this.stream = null;
    this.live.set(null);
    this.error.set(null);
    this.chosenModel.set(null);
    this.conversation.set(null);
    this.messages.set([]);

    this.api.getConversation(conversationId).subscribe({
      next: (conversation) => this.conversation.set(conversation),
      error: (error: unknown) => this.error.set(describeApiError(error)),
    });
    this.reloadMessages(conversationId, () => {
      // 從首頁 / 專案頁「直接開聊」：送出暫存的第一則訊息
      const pending = this.pending.take(conversationId);
      if (pending) {
        if (pending.modelId) {
          this.chosenModel.set(pending.modelId);
        }
        this.send(pending.prompt, pending.modelId, pending.makeTopicId);
      }
    });
  }

  private finish(conversationId: string, error: string | null): void {
    this.error.set(error);
    this.reloadMessages(conversationId, () => this.live.set(null));
  }

  private reloadMessages(conversationId: string, after?: () => void): void {
    this.api.listMessages(conversationId).subscribe({
      next: (messages) => {
        if (conversationId !== this.conversationId()) {
          return;
        }
        this.messages.set(messages);
        after?.();
      },
      error: (error: unknown) => this.error.set(describeApiError(error)),
    });
  }
}
