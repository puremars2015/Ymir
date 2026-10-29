import { computed, inject, Injectable, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { ApiService, describeApiError } from '../api/api.service';
import {
  DirectContact,
  DirectMessage,
  DirectMessagesRead,
  GroupMessage,
  GroupRead,
} from '../api/api-types';
import { UserEventStream } from '../events/user-event-stream';
import { GroupUpdated } from './group-events';
import {
  applyMessageToContacts,
  applyReadReceipt,
  clearUnread,
  mergeMessages,
  peerOf,
  sortContacts,
  totalUnread,
} from './direct-message-state';
import { GroupChatStore } from './group-chat.store';

/**
 * 使用者之間的訊息（ADR-0023）：右側成員名單、目前開啟的一對一對話，以及即時通知
 * （共用的使用者事件串流 `/api/events`，ADR-0032 §2）。
 */
@Injectable({ providedIn: 'root' })
export class DirectMessageStore {
  private readonly api = inject(ApiService);
  /** 群組聊天（ADR-0026）的事件也在同一條 SSE 連線。 */
  private readonly groups = inject(GroupChatStore);
  private readonly stream = inject(UserEventStream);
  private unsubscribe: (() => void)[] = [];
  private myId: string | null = null;

  readonly contacts = signal<DirectContact[]>([]);
  /** 目前開啟的對話對象；null 表示顯示名單。 */
  readonly openPeerId = signal<string | null>(null);
  readonly messages = signal<DirectMessage[]>([]);
  readonly hasMore = signal(false);
  readonly loading = signal(false);
  readonly connected = signal(false);
  readonly error = signal<string | null>(null);
  /** 面板是否看得到訊息（面板收起或分頁在背景時，新訊息不自動標為已讀）。 */
  readonly visible = signal(true);

  /** 工具列的未讀總數：一對一訊息 + 群組。 */
  readonly unreadTotal = computed(() => totalUnread(this.contacts()) + this.groups.unreadTotal());
  readonly openPeer = computed(
    () => this.contacts().find((c) => c.userId === this.openPeerId()) ?? null,
  );

  /**
   * 登入後呼叫（只限可以使用訊息的角色）；重複呼叫無作用。事件來自共用的 `/api/events`（ADR-0032 §2），
   * 每次連上（ready）都重新讀取名單與目前對話，補齊斷線期間的訊息。
   */
  connect(myId: string): void {
    if (this.unsubscribe.length && this.myId === myId) return;
    this.disconnect();
    this.myId = myId;
    this.groups.connect(myId);
    this.unsubscribe = [
      this.stream.on('ready', () => {
        this.connected.set(true);
        this.refreshContacts();
        this.reloadOpenConversation();
        this.groups.refresh();
      }),
      this.stream.on('message', (data) => this.onMessage(JSON.parse(data) as DirectMessage)),
      this.stream.on('read', (data) => this.onRead(JSON.parse(data) as DirectMessagesRead)),
      this.stream.on('group-message', (data) =>
        this.groups.onMessage(JSON.parse(data) as GroupMessage),
      ),
      this.stream.on('group-updated', (data) =>
        this.groups.onUpdated(JSON.parse(data) as GroupUpdated),
      ),
      this.stream.on('group-read', (data) => this.groups.onRead(JSON.parse(data) as GroupRead)),
    ];
    // 已經連上的串流不會再送 ready：直接讀一次。
    if (this.stream.connected() || !this.stream.supported) {
      this.connected.set(this.stream.connected());
      this.refreshContacts();
      this.groups.refresh();
    }
  }

  /** 登出（或改成訪客）時取消訂閱並清掉資料；連線本身由 UserEventStream 管理。 */
  disconnect(): void {
    for (const off of this.unsubscribe) off();
    this.unsubscribe = [];
    this.myId = null;
    this.groups.disconnect();
    this.connected.set(false);
    this.contacts.set([]);
    this.openPeerId.set(null);
    this.messages.set([]);
    this.hasMore.set(false);
  }

  refreshContacts(): void {
    this.api.listDirectContacts().subscribe({
      next: (contacts) => this.contacts.set(sortContacts(contacts)),
      error: (e: unknown) => this.error.set(describeApiError(e)),
    });
  }

  open(peerId: string): void {
    this.openPeerId.set(peerId);
    this.messages.set([]);
    this.hasMore.set(false);
    this.error.set(null);
    this.reloadOpenConversation();
  }

  close(): void {
    this.openPeerId.set(null);
    this.messages.set([]);
    this.hasMore.set(false);
  }

  loadOlder(): void {
    const peerId = this.openPeerId();
    const first = this.messages()[0];
    if (!peerId || !first || this.loading()) return;
    this.loading.set(true);
    this.api.listDirectMessages(peerId, first.id).subscribe({
      next: (page) => {
        if (this.openPeerId() !== peerId) return;
        this.messages.update((list) => mergeMessages(list, page.messages));
        this.hasMore.set(page.hasMore);
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.loading.set(false);
        this.error.set(describeApiError(e));
      },
    });
  }

  send(text: string): Observable<DirectMessage> {
    const peerId = this.openPeerId();
    if (!peerId) throw new Error('沒有開啟的對話');
    this.error.set(null);
    return this.api.sendDirectMessage(peerId, text).pipe(tap((message) => this.onMessage(message)));
  }

  /** 面板重新顯示時，把目前對話的未讀補標為已讀。 */
  setVisible(visible: boolean): void {
    this.visible.set(visible);
    this.groups.setVisible(visible);
    const peerId = this.openPeerId();
    if (
      visible &&
      peerId &&
      this.contacts().some((c) => c.userId === peerId && Number(c.unreadCount) > 0)
    ) {
      this.markRead(peerId);
    }
  }

  private reloadOpenConversation(): void {
    const peerId = this.openPeerId();
    if (!peerId) return;
    this.loading.set(true);
    this.api.listDirectMessages(peerId).subscribe({
      next: (page) => {
        if (this.openPeerId() !== peerId) return;
        this.messages.update((list) => mergeMessages(list, page.messages));
        this.hasMore.set(page.hasMore);
        this.loading.set(false);
        if (this.visible()) this.markRead(peerId);
      },
      error: (e: unknown) => {
        this.loading.set(false);
        this.error.set(describeApiError(e));
      },
    });
  }

  private markRead(peerId: string): void {
    this.contacts.update((list) => clearUnread(list, peerId));
    this.api.markDirectMessagesRead(peerId).subscribe({ error: () => undefined });
  }

  private onMessage(message: DirectMessage): void {
    const myId = this.myId;
    if (!myId) return;
    const peerId = peerOf(message, myId);
    const openPeerId = this.visible() ? this.openPeerId() : null;
    const known = this.contacts().some((c) => c.userId === peerId);
    this.contacts.update((list) => applyMessageToContacts(list, message, myId, openPeerId));
    if (!known) this.refreshContacts();
    if (this.openPeerId() === peerId) {
      this.messages.update((list) => mergeMessages(list, [message]));
      if (message.recipientId === myId && this.visible()) this.markRead(peerId);
    }
  }

  private onRead(read: DirectMessagesRead): void {
    if (read.readerId === this.myId) {
      // 自己在另一個分頁讀了
      this.contacts.update((list) => clearUnread(list, read.peerId));
    } else {
      this.messages.update((list) => applyReadReceipt(list, read));
    }
  }
}
