import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  Injector,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { Observable } from 'rxjs';
import { describeApiError } from '../core/api/api.service';
import { GroupMessage } from '../core/api/api-types';
import { AuthService } from '../core/auth/auth.service';
import {
  DIRECT_MESSAGE_MAX_LENGTH,
  filterContacts,
  formatMessageTime,
  normalizeDirectMessage,
} from '../core/messaging/direct-message-state';
import { DirectMessageStore } from '../core/messaging/direct-message.store';
import { addAttachments } from '../core/attachments/attachment-rules';
import { formatSize } from '../core/files/workspace-files';
import {
  GROUP_MAX_MEMBERS,
  GROUP_NAME_MAX_LENGTH,
  groupSubmissionText,
  isBotCommand,
  normalizeGroupName,
  senderLabel,
} from '../core/messaging/group-chat-state';
import { GroupChatStore } from '../core/messaging/group-chat.store';
import { AssistantText } from '../shared/assistant-text';

type GroupView = 'list' | 'create' | 'thread' | 'manage';

/**
 * 成員面板的「群組」分頁（ADR-0026）：群組清單、建立、群組聊天與 @bot、成員管理。
 * 訊息與 Bot 回答一律以文字綁定顯示（不經 innerHTML），內容由使用者與 Agent 產生、不可信任。
 */
@Component({
  selector: 'app-group-chat',
  imports: [AssistantText],
  templateUrl: './group-chat.html',
  styleUrl: './group-chat.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroupChat {
  protected readonly store = inject(GroupChatStore);
  protected readonly people = inject(DirectMessageStore);
  private readonly auth = inject(AuthService);
  private readonly injector = inject(Injector);
  private readonly thread = viewChild<ElementRef<HTMLElement>>('thread');
  private readonly draftInput = viewChild<ElementRef<HTMLTextAreaElement>>('draftInput');

  readonly closed = output<void>();
  readonly showPeople = output<void>();

  protected readonly maxLength = DIRECT_MESSAGE_MAX_LENGTH;
  protected readonly nameMaxLength = GROUP_NAME_MAX_LENGTH;
  protected readonly view = signal<GroupView>('list');
  protected readonly draft = signal('');
  protected readonly sending = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly newName = signal('');
  protected readonly memberQuery = signal('');
  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly renameText = signal('');
  protected readonly confirmArchive = signal(false);
  /** 要給 @bot 的附件（送出時才上傳到群組 Agent 的工作目錄）。 */
  protected readonly files = signal<readonly File[]>([]);
  protected readonly dragging = signal(false);

  protected readonly myId = computed(() => this.auth.user()?.id ?? null);
  /** 有附件時一定會送給 @bot（`groupSubmissionText` 自動補上）。 */
  protected readonly isBot = computed(() => this.files().length > 0 || isBotCommand(this.draft()));
  protected readonly submission = computed(() =>
    groupSubmissionText(this.draft(), this.files().length),
  );
  protected readonly canSend = computed(() => {
    const text = this.submission();
    return (
      !this.sending() &&
      text !== null &&
      normalizeDirectMessage(text) !== null &&
      (!isBotCommand(text) || text.trim().length > '@bot'.length)
    );
  });
  protected readonly memberIds = computed(
    () => new Set(this.store.detail()?.members.map((m) => m.userId) ?? []),
  );
  /** 建立或加入成員時可選的人（排除已在群組裡的）。 */
  protected readonly candidates = computed(() =>
    filterContacts(this.people.contacts(), this.memberQuery()).filter(
      (c) => !this.memberIds().has(c.userId),
    ),
  );
  protected readonly canCreate = computed(
    () =>
      !this.sending() &&
      normalizeGroupName(this.newName()) !== null &&
      this.selected().size + 1 <= GROUP_MAX_MEMBERS,
  );

  private lastCount = 0;

  constructor() {
    // 新訊息進來時捲到最下面（與一對一訊息相同）。
    effect(() => {
      const messages = this.store.messages();
      this.store.live();
      const grew = messages.length > this.lastCount;
      const firstLoad = this.lastCount === 0;
      this.lastCount = messages.length;
      afterNextRender(
        () => {
          const element = this.thread()?.nativeElement;
          if (!element) return;
          const nearBottom = element.scrollHeight - element.scrollTop - element.clientHeight < 160;
          if (firstLoad || nearBottom || (grew && messages.at(-1)?.senderId === this.myId())) {
            element.scrollTop = element.scrollHeight;
          }
        },
        { injector: this.injector },
      );
    });
    // 群組被封存或自己被移除時回到清單。
    effect(() => {
      if (!this.store.openGroupId() && (this.view() === 'thread' || this.view() === 'manage')) {
        this.view.set('list');
      }
    });
  }

  protected initial(name: string): string {
    return name.trim().charAt(0) || '?';
  }

  protected time(iso: string): string {
    return formatMessageTime(iso);
  }

  protected unread(count: number | string): number {
    return Number(count);
  }

  protected label(message: GroupMessage): string {
    return senderLabel(message);
  }

  protected liveText(message: GroupMessage): string {
    return message.executionId ? (this.store.live()[message.executionId]?.text ?? '') : '';
  }

  protected liveStatus(message: GroupMessage): string {
    const view = message.executionId ? this.store.live()[message.executionId] : undefined;
    if (view?.toolBusy) return '正在工作中......';
    return view?.statusText ?? '正在思考中......';
  }

  protected openGroup(groupId: string): void {
    this.lastCount = 0;
    this.draft.set('');
    this.files.set([]);
    this.notice.set(null);
    this.actionError.set(null);
    this.store.open(groupId);
    this.view.set('thread');
    afterNextRender(() => this.draftInput()?.nativeElement.focus(), { injector: this.injector });
  }

  protected backToList(): void {
    this.lastCount = 0;
    this.store.close();
    this.view.set('list');
  }

  protected startCreate(): void {
    this.newName.set('');
    this.memberQuery.set('');
    this.selected.set(new Set());
    this.actionError.set(null);
    this.view.set('create');
  }

  protected toggle(userId: string): void {
    this.selected.update((set) => {
      const next = new Set(set);
      if (next.has(userId)) next.delete(userId);
      else next.add(userId);
      return next;
    });
  }

  protected create(): void {
    const name = normalizeGroupName(this.newName());
    if (!name || !this.canCreate()) return;
    this.sending.set(true);
    this.store.create(name, [...this.selected()]).subscribe({
      next: (group) => {
        this.sending.set(false);
        this.openGroup(group.id);
      },
      error: (e: unknown) => {
        this.sending.set(false);
        this.actionError.set(describeApiError(e));
      },
    });
  }

  protected manage(): void {
    this.renameText.set(this.store.detail()?.name ?? '');
    this.memberQuery.set('');
    this.selected.set(new Set());
    this.confirmArchive.set(false);
    this.actionError.set(null);
    this.view.set('manage');
  }

  protected rename(): void {
    const name = normalizeGroupName(this.renameText());
    if (!name) return;
    this.run(this.store.rename(name), '已更新群組名稱。');
  }

  protected addSelected(): void {
    if (this.selected().size === 0) return;
    this.run(this.store.addMembers([...this.selected()]), '已加入成員。', () =>
      this.selected.set(new Set()),
    );
  }

  protected remove(userId: string): void {
    this.run(this.store.removeMember(userId), '已移除成員。');
  }

  protected leave(): void {
    const myId = this.myId();
    if (myId) this.run(this.store.removeMember(myId), null, () => this.view.set('list'));
  }

  protected archive(): void {
    if (!this.confirmArchive()) {
      this.confirmArchive.set(true);
      return;
    }
    this.run(this.store.archive(), null, () => this.view.set('list'));
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      this.send();
    }
  }

  protected size(file: File): string {
    return formatSize(file.size);
  }

  protected onFilesChosen(input: HTMLInputElement): void {
    this.addFiles([...(input.files ?? [])]);
    input.value = ''; // 同一個檔案移除後可以再選一次
  }

  protected removeFile(index: number): void {
    this.files.update((files) => files.filter((_, i) => i !== index));
  }

  protected onDragOver(event: DragEvent): void {
    if (event.dataTransfer?.types.includes('Files')) {
      event.preventDefault();
      this.dragging.set(true);
    }
  }

  protected onDrop(event: DragEvent): void {
    this.dragging.set(false);
    if (!event.dataTransfer?.files.length) return;
    event.preventDefault();
    this.addFiles([...event.dataTransfer.files]);
  }

  /** 貼上圖片（例如截圖）也當作附件；貼上文字照常。 */
  protected onPaste(event: ClipboardEvent): void {
    const pasted = [...(event.clipboardData?.files ?? [])];
    if (pasted.length === 0) return;
    event.preventDefault();
    this.addFiles(pasted);
  }

  private addFiles(incoming: File[]): void {
    if (incoming.length === 0 || this.sending()) return;
    const result = addAttachments(this.files(), incoming);
    this.files.set(result.files);
    this.notice.set(result.rejected.length ? `已略過：${result.rejected.join('、')}` : null);
  }

  protected send(): void {
    const text = this.submission();
    if (!text || !this.canSend()) return;
    this.sending.set(true);
    this.notice.set(null);
    this.store.send(text, this.files()).subscribe({
      next: (result) => {
        this.draft.set('');
        this.files.set([]);
        this.sending.set(false);
        // 訊息已送出，但 @bot 無法執行（忙碌、配額、模型）：顯示原因。
        this.notice.set(result.botProblem ?? null);
        afterNextRender(() => this.draftInput()?.nativeElement.focus(), {
          injector: this.injector,
        });
      },
      error: (e: unknown) => {
        this.sending.set(false);
        this.notice.set(describeApiError(e));
      },
    });
  }

  private run(action: Observable<unknown>, success: string | null, after?: () => void): void {
    this.sending.set(true);
    this.actionError.set(null);
    action.subscribe({
      next: () => {
        this.sending.set(false);
        this.notice.set(success);
        after?.();
      },
      error: (e: unknown) => {
        this.sending.set(false);
        this.actionError.set(describeApiError(e));
      },
    });
  }
}
