import { computed, inject, Injectable, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { ApiService, describeApiError } from '../api/api.service';
import { DirectContact, DirectMessage, DirectMessagesRead } from '../api/api-types';
import {
  applyMessageToContacts,
  applyReadReceipt,
  clearUnread,
  mergeMessages,
  peerOf,
  sortContacts,
  totalUnread,
} from './direct-message-state';

/**
 * 使用者之間的訊息（ADR-0023）：右側成員名單、目前開啟的一對一對話，以及 `/api/direct-messages/events` 的即時通知。
 * 認證靠同源 cookie（ADR-0002），所以直接用原生 EventSource；斷線時瀏覽器會自動重連，
 * 每次連上（ready）都重新讀取名單與目前對話，補齊斷線期間的訊息。
 */
@Injectable({ providedIn: 'root' })
export class DirectMessageStore {
  private readonly api = inject(ApiService);
  private source: EventSource | null = null;
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

  readonly unreadTotal = computed(() => totalUnread(this.contacts()));
  readonly openPeer = computed(
    () => this.contacts().find((c) => c.userId === this.openPeerId()) ?? null,
  );

  /** 登入後呼叫；重複呼叫無作用。 */
  connect(myId: string): void {
    if (this.source && this.myId === myId) return;
    this.disconnect();
    this.myId = myId;
    if (typeof EventSource === 'undefined') {
      this.refreshContacts();
      return;
    }
    const source = new EventSource('/api/direct-messages/events', { withCredentials: true });
    this.source = source;
    // zoneless：signal 更新即會觸發畫面更新，不需要 NgZone。
    source.addEventListener('ready', () => {
      this.connected.set(true);
      this.refreshContacts();
      this.reloadOpenConversation();
    });
    source.addEventListener('message', (event: MessageEvent<string>) =>
      this.onMessage(JSON.parse(event.data) as DirectMessage),
    );
    source.addEventListener('read', (event: MessageEvent<string>) =>
      this.onRead(JSON.parse(event.data) as DirectMessagesRead),
    );
    source.onerror = () => this.connected.set(false);
  }

  /** 登出時關閉連線並清掉資料。 */
  disconnect(): void {
    this.source?.close();
    this.source = null;
    this.myId = null;
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
