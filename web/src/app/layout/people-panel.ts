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
import { describeApiError } from '../core/api/api.service';
import { AuthService } from '../core/auth/auth.service';
import { DirectMessageStore } from '../core/messaging/direct-message.store';
import { GroupChatStore } from '../core/messaging/group-chat.store';
import { GroupChat } from './group-chat';
import {
  DIRECT_MESSAGE_MAX_LENGTH,
  filterContacts,
  formatMessageTime,
  normalizeDirectMessage,
} from '../core/messaging/direct-message-state';

/**
 * 右側成員面板（ADR-0023）：列出公司內的其他使用者，點選後在面板內一對一聊天（類似簡易版 Teams）。
 * 訊息一律以文字綁定顯示（不經 innerHTML），內容由使用者輸入、不可信任。
 */
@Component({
  selector: 'app-people-panel',
  imports: [GroupChat],
  templateUrl: './people-panel.html',
  styleUrl: './people-panel.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PeoplePanel {
  protected readonly store = inject(DirectMessageStore);
  protected readonly groups = inject(GroupChatStore);
  protected readonly auth = inject(AuthService);
  private readonly injector = inject(Injector);
  private readonly thread = viewChild<ElementRef<HTMLElement>>('thread');
  private readonly draftInput = viewChild<ElementRef<HTMLTextAreaElement>>('draftInput');

  /** 關閉面板（窄螢幕的抽屜、或桌面收合）。 */
  readonly closed = output<void>();

  protected readonly maxLength = DIRECT_MESSAGE_MAX_LENGTH;
  /** 成員（一對一）或群組（ADR-0026）。 */
  protected readonly tab = signal<'people' | 'groups'>('people');
  protected readonly peopleUnread = computed(() =>
    this.store.contacts().reduce((sum, c) => sum + Number(c.unreadCount), 0),
  );
  protected readonly query = signal('');
  protected readonly draft = signal('');
  protected readonly sending = signal(false);
  protected readonly sendError = signal<string | null>(null);
  protected readonly myId = computed(() => this.auth.user()?.id ?? null);
  protected readonly visibleContacts = computed(() =>
    filterContacts(this.store.contacts(), this.query()),
  );
  protected readonly canSend = computed(
    () => !this.sending() && normalizeDirectMessage(this.draft()) !== null,
  );
  /** 最後一則我送出且對方已讀的訊息（只在那一則下方顯示「已讀」）。 */
  protected readonly lastReadId = computed(() => {
    const myId = this.myId();
    const mine = this.store.messages().filter((m) => m.senderId === myId && m.readAt);
    return mine.at(-1)?.id ?? null;
  });

  private lastCount = 0;

  constructor() {
    // 新訊息進來時捲到最下面；往上讀較早的訊息時（插在前面）不要跳動。
    effect(() => {
      const messages = this.store.messages();
      const grewAtEnd = messages.length > this.lastCount;
      const firstLoad = this.lastCount === 0;
      this.lastCount = messages.length;
      if (!grewAtEnd) return;
      afterNextRender(
        () => {
          const element = this.thread()?.nativeElement;
          if (!element) return;
          const nearBottom = element.scrollHeight - element.scrollTop - element.clientHeight < 160;
          if (firstLoad || nearBottom || messages.at(-1)?.senderId === this.myId()) {
            element.scrollTop = element.scrollHeight;
          }
        },
        { injector: this.injector },
      );
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

  protected openChat(peerId: string): void {
    this.lastCount = 0;
    this.draft.set('');
    this.sendError.set(null);
    this.store.open(peerId);
    afterNextRender(() => this.draftInput()?.nativeElement.focus(), { injector: this.injector });
  }

  protected back(): void {
    this.lastCount = 0;
    this.store.close();
  }

  protected onKeydown(event: KeyboardEvent): void {
    // Enter 送出、Shift+Enter 換行；輸入法選字中的 Enter 不送出。
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      this.send();
    }
  }

  protected send(): void {
    const text = normalizeDirectMessage(this.draft());
    if (!text || this.sending()) return;
    this.sending.set(true);
    this.sendError.set(null);
    this.store.send(text).subscribe({
      next: () => {
        this.draft.set('');
        this.sending.set(false);
        afterNextRender(() => this.draftInput()?.nativeElement.focus(), {
          injector: this.injector,
        });
      },
      error: (e: unknown) => {
        this.sending.set(false);
        this.sendError.set(describeApiError(e));
      },
    });
  }
}
