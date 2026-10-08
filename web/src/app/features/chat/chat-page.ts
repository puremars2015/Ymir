import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  signal,
  untracked,
} from '@angular/core';
import { ComposerSubmission } from '../../core/make/make-command';
import { RouterLink } from '@angular/router';
import { concatMap, forkJoin, from, Observable, of, Subscription, switchMap, toArray } from 'rxjs';
import { ApiService, describeApiError } from '../../core/api/api.service';
import {
  ArtifactGroup,
  Attachment,
  ChatMessage,
  Conversation,
  WorkspaceFile,
} from '../../core/api/api-types';
import {
  applyExecutionEvent,
  ExecutionView,
  initialExecutionView,
} from '../../core/executions/execution-state';
import { ExecutionStreamService } from '../../core/executions/execution-stream.service';
import { resumeTurnFrom } from '../../core/executions/resume-turn';
import { normalizeTitle } from '../../core/navigation/navigation-edits';
import { resolveModel } from '../../core/models/model-selection';
import { ModelStore } from '../../core/models/model.store';
import { NavigationStore } from '../../core/navigation/navigation.store';
import { PendingPromptService } from '../../core/navigation/pending-prompt.service';
import { Composer } from '../../shared/composer';
import { AssistantText } from '../../shared/assistant-text';
import { Markdown } from '../../shared/markdown';
import { ModelPicker } from '../../shared/model-picker';
import { AttachmentList } from '../../shared/attachment-list';
import { FilesPanel } from './files-panel';
import { ArtifactDownload } from '../../shared/artifact-download';

interface LiveTurn {
  prompt: string;
  attachments: Attachment[];
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
  host: { class: 'chat-surface' },
  imports: [
    RouterLink,
    Composer,
    ModelPicker,
    AssistantText,
    Markdown,
    FilesPanel,
    AttachmentList,
    ArtifactDownload,
  ],
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
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  readonly conversationId = input.required<string>();
  protected readonly conversation = signal<Conversation | null>(null);
  protected readonly messages = signal<ChatMessage[]>([]);
  protected readonly live = signal<LiveTurn | null>(null);
  protected readonly error = signal<string | null>(null);
  /** 送出前正在上傳附件（顯示進度、輸入框暫停）。 */
  protected readonly uploading = signal<string | null>(null);
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

  /** Agent 在工作目錄產生的檔案（專案內的對話共用，ADR-0007）。 */
  protected readonly files = signal<WorkspaceFile[]>([]);
  protected readonly filesTruncated = signal(false);
  protected readonly filesLoading = signal(false);
  protected readonly filesError = signal<string | null>(null);
  protected readonly filesOpen = signal(false);
  protected readonly artifacts = signal<ArtifactGroup[]>([]);
  protected readonly artifactCount = computed(() =>
    this.artifacts().reduce((sum, group) => sum + group.files.length, 0),
  );
  protected readonly artifactByMessage = computed(
    () =>
      new Map(
        this.artifacts()
          .filter((group) => group.conversationId === this.conversationId())
          .map((group) => [group.messageId, group]),
      ),
  );

  protected readonly editingTitle = signal(false);
  protected readonly titleDraft = signal('');
  protected readonly copiedId = signal<string | null>(null);

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
    this.send(submission.content, this.selectedModel(), submission.makeTopicId, submission.files);
  }

  protected send(
    prompt: string,
    modelId: string | null = this.selectedModel(),
    makeTopicId: string | null = null,
    files: File[] = [],
    thinkingLevel: string | null = this.modelStore.thinkingFor(modelId),
  ): void {
    if (this.live() || this.uploading()) {
      return;
    }

    const conversationId = this.conversationId();
    this.error.set(null);
    let attachments: Attachment[] = [];
    this.upload(conversationId, files)
      .pipe(
        switchMap((uploaded) => {
          attachments = uploaded;
          this.uploading.set(null);
          return this.api.sendMessage(
            conversationId,
            prompt,
            modelId,
            makeTopicId,
            uploaded.map((a) => a.id),
            crypto.randomUUID(),
            thinkingLevel,
          );
        }),
      )
      .subscribe({
        next: (accepted) => {
          if (conversationId !== this.conversationId()) {
            return; // 送出後使用者已切到別的對話；執行在背景繼續，回來時看歷史即可
          }
          this.store.touch(conversationId);
          this.attach(
            conversationId,
            prompt,
            accepted.executionId,
            accepted.eventStreamUrl,
            attachments,
          );
        },
        error: (error: unknown) => {
          this.uploading.set(null);
          this.error.set(describeApiError(error));
        },
      });
  }

  /** 依序上傳附件（一次一個，避免大檔同時佔用頻寬）；沒有附件時直接完成。 */
  private upload(conversationId: string, files: File[]): Observable<Attachment[]> {
    if (files.length === 0) {
      return of([]);
    }
    let done = 0;
    this.uploading.set(`正在上傳檔案（0 / ${files.length}）…`);
    return from(files).pipe(
      concatMap((file) =>
        this.api.uploadAttachment(conversationId, file).pipe(
          switchMap((attachment) => {
            done++;
            this.uploading.set(`正在上傳檔案（${done} / ${files.length}）…`);
            return of(attachment);
          }),
        ),
      ),
      toArray(),
    );
  }

  /** 訂閱 execution 的 SSE，以 live turn 顯示；送出新訊息與重新接回執行中的工作共用。 */
  private attach(
    conversationId: string,
    prompt: string,
    executionId: string,
    url: string,
    attachments: Attachment[] = [],
  ): void {
    this.live.set({ prompt, attachments, executionId, view: initialExecutionView() });
    this.stream = this.streams.stream(url).subscribe({
      next: (event) =>
        this.live.update((turn) =>
          turn ? { ...turn, view: applyExecutionEvent(turn.view, event) } : turn,
        ),
      error: (error: unknown) => this.finish(conversationId, describeApiError(error)),
      complete: () => this.finish(conversationId, null),
    });
  }

  /** 標題點兩下改名；Enter / 離開輸入框存檔，Esc 取消。 */
  protected startRename(): void {
    const title = this.conversation()?.title;
    if (title !== undefined) {
      this.titleDraft.set(title);
      this.editingTitle.set(true);
      afterNextRender(
        () => {
          const input = this.host.nativeElement.querySelector<HTMLInputElement>('.title-input');
          input?.focus();
          input?.select();
        },
        { injector: this.injector },
      );
    }
  }

  protected commitRename(): void {
    if (!this.editingTitle()) {
      return;
    }
    this.editingTitle.set(false);
    const conversation = this.conversation();
    const title = normalizeTitle(this.titleDraft());
    if (!conversation || !title || title === conversation.title) {
      return;
    }
    this.conversation.set({ ...conversation, title });
    this.store.renameConversation(conversation.id, title).subscribe({
      error: (error: unknown) => {
        this.conversation.set(conversation);
        this.error.set(describeApiError(error));
      },
    });
  }

  /** 複製 Agent 回覆的 Markdown 原文。 */
  protected copy(message: ChatMessage, text: string): void {
    void navigator.clipboard?.writeText(text).then(() => {
      this.copiedId.set(message.id);
      setTimeout(() => {
        if (this.copiedId() === message.id) {
          this.copiedId.set(null);
        }
      }, 1500);
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
    this.uploading.set(null);
    this.chosenModel.set(null);
    this.conversation.set(null);
    this.editingTitle.set(false);
    this.messages.set([]);
    this.files.set([]);
    this.artifacts.set([]);
    this.refreshFiles();

    forkJoin([
      this.api.getConversation(conversationId),
      this.api.listMessages(conversationId),
    ]).subscribe({
      next: ([conversation, messages]) => {
        if (conversationId !== this.conversationId()) {
          return;
        }
        this.conversation.set(conversation);
        // 重新整理或切回來時 Agent 仍在執行：接回它的串流（事件從頭重播）
        const resumed = resumeTurnFrom(messages, conversation.activeExecutionId);
        if (resumed) {
          this.messages.set(resumed.history);
          this.attach(
            conversationId,
            resumed.prompt,
            resumed.executionId,
            `/api/executions/${resumed.executionId}/events`,
            resumed.attachments,
          );
          return;
        }
        this.messages.set(messages);
        // 從首頁 / 專案頁「直接開聊」：送出暫存的第一則訊息
        const pending = this.pending.take(conversationId);
        if (pending) {
          if (pending.modelId) {
            this.chosenModel.set(pending.modelId);
          }
          this.send(
            pending.prompt,
            pending.modelId,
            pending.makeTopicId,
            pending.files,
            pending.thinkingLevel ?? null,
          );
        }
      },
      error: (error: unknown) => this.error.set(describeApiError(error)),
    });
  }

  private finish(conversationId: string, error: string | null): void {
    if (conversationId !== this.conversationId()) return;
    this.error.set(error);
    this.reloadMessages(conversationId, () => this.live.set(null));
    this.refreshFiles();
  }

  protected refreshFiles(): void {
    const conversationId = this.conversationId();
    this.filesLoading.set(true);
    this.filesError.set(null);
    forkJoin([
      this.api.listConversationFiles(conversationId),
      this.api.listConversationArtifacts(conversationId),
    ]).subscribe({
      next: ([result, artifacts]) => {
        if (conversationId !== this.conversationId()) {
          return;
        }
        this.files.set(result.files);
        this.filesTruncated.set(result.truncated);
        this.filesLoading.set(false);
        this.artifacts.set(artifacts);
      },
      error: (error: unknown) => {
        if (conversationId !== this.conversationId()) return;
        this.filesLoading.set(false);
        this.filesError.set(describeApiError(error));
      },
    });
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
