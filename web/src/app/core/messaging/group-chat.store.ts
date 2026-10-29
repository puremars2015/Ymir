import { computed, inject, Injectable, signal } from '@angular/core';
import { concatMap, from, Observable, of, Subscription, switchMap, tap, toArray } from 'rxjs';
import { ApiService, describeApiError } from '../api/api.service';
import {
  ChatGroup,
  ChatGroupSummary,
  GroupMessage,
  GroupRead,
  SendGroupMessageResult,
} from '../api/api-types';
import { GroupUpdated } from './group-events';
import {
  applyExecutionEvent,
  ExecutionView,
  initialExecutionView,
} from '../executions/execution-state';
import { ExecutionStreamService } from '../executions/execution-stream.service';
import {
  applyGroupMessage,
  clearGroupUnread,
  mergeGroupMessages,
  sortGroups,
  totalGroupUnread,
} from './group-chat-state';

/**
 * 群組聊天與 @bot（ADR-0026）。即時通知與一對一訊息共用 `/api/events`
 * （由 {@link DirectMessageStore} 轉給這裡）；Pending 的 Bot 訊息以群組的串流端點顯示即時回答。
 */
@Injectable({ providedIn: 'root' })
export class GroupChatStore {
  private readonly api = inject(ApiService);
  private readonly streams = inject(ExecutionStreamService);
  private myId: string | null = null;
  private readonly liveSubscriptions = new Map<string, Subscription>();

  readonly groups = signal<ChatGroupSummary[]>([]);
  readonly openGroupId = signal<string | null>(null);
  readonly detail = signal<ChatGroup | null>(null);
  readonly messages = signal<GroupMessage[]>([]);
  readonly hasMore = signal(false);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  /** Pending 的 Bot 訊息即時狀態（以 execution id 為鍵）。 */
  readonly live = signal<Record<string, ExecutionView>>({});
  /** 面板是否看得到訊息（收起或分頁在背景時不自動標為已讀）。 */
  readonly visible = signal(true);

  readonly unreadTotal = computed(() => totalGroupUnread(this.groups()));
  readonly openGroup = computed(
    () => this.groups().find((g) => g.id === this.openGroupId()) ?? null,
  );
  readonly isOwner = computed(() => {
    const detail = this.detail();
    return !!detail && detail.ownerId === this.myId;
  });

  connect(myId: string): void {
    this.myId = myId;
  }

  disconnect(): void {
    this.myId = null;
    this.stopAllLive();
    this.groups.set([]);
    this.close();
  }

  /** SSE 連上（ready）時重新讀取，補齊斷線期間的訊息。 */
  refresh(): void {
    this.api.listChatGroups().subscribe({
      next: (groups) => this.groups.set(sortGroups(groups)),
      error: (e: unknown) => this.error.set(describeApiError(e)),
    });
    this.reloadOpen();
  }

  open(groupId: string): void {
    this.stopAllLive();
    this.openGroupId.set(groupId);
    this.messages.set([]);
    this.detail.set(null);
    this.hasMore.set(false);
    this.error.set(null);
    this.reloadOpen();
  }

  close(): void {
    this.stopAllLive();
    this.openGroupId.set(null);
    this.detail.set(null);
    this.messages.set([]);
    this.hasMore.set(false);
  }

  create(name: string, memberIds: string[]): Observable<ChatGroup> {
    return this.api.createChatGroup(name, memberIds).pipe(tap(() => this.refresh()));
  }

  rename(name: string): Observable<ChatGroup> {
    return this.api
      .renameChatGroup(this.requireOpen(), name)
      .pipe(tap((group) => this.applyDetail(group)));
  }

  addMembers(userIds: string[]): Observable<ChatGroup> {
    return this.api
      .addChatGroupMembers(this.requireOpen(), userIds)
      .pipe(tap((group) => this.applyDetail(group)));
  }

  removeMember(userId: string): Observable<void> {
    const groupId = this.requireOpen();
    return this.api.removeChatGroupMember(groupId, userId).pipe(
      tap(() => {
        if (userId === this.myId) {
          this.dropGroup(groupId);
        } else {
          this.api.getChatGroup(groupId).subscribe((group) => this.applyDetail(group));
        }
      }),
    );
  }

  archive(): Observable<void> {
    const groupId = this.requireOpen();
    return this.api.archiveChatGroup(groupId).pipe(tap(() => this.dropGroup(groupId)));
  }

  loadOlder(): void {
    const groupId = this.openGroupId();
    const first = this.messages()[0];
    if (!groupId || !first || this.loading()) return;
    this.loading.set(true);
    this.api.listGroupMessages(groupId, first.id).subscribe({
      next: (page) => {
        if (this.openGroupId() !== groupId) return;
        this.messages.update((list) => mergeGroupMessages(list, page.messages));
        this.hasMore.set(page.hasMore);
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.loading.set(false);
        this.error.set(describeApiError(e));
      },
    });
  }

  /** 送出訊息；有附件時先依序上傳到群組 Agent 的工作目錄，再隨 @bot 訊息送出。 */
  send(text: string, files: readonly File[] = []): Observable<SendGroupMessageResult> {
    const groupId = this.requireOpen();
    this.error.set(null);
    // 依序上傳（不同時寫入同一個 runtime），全部成功才送出訊息。
    const uploads = files.length
      ? from(files).pipe(
          concatMap((file) => this.api.uploadGroupAttachment(groupId, file)),
          toArray(),
        )
      : of([]);
    return uploads.pipe(
      switchMap((attachments) =>
        this.api.sendGroupMessage(
          groupId,
          text,
          crypto.randomUUID(),
          attachments.map((a) => a.id),
        ),
      ),
      tap((result) => {
        this.onMessage(result.message);
        if (result.bot) this.onMessage(result.bot);
      }),
    );
  }

  setVisible(visible: boolean): void {
    this.visible.set(visible);
    const groupId = this.openGroupId();
    if (visible && groupId && Number(this.openGroup()?.unreadCount ?? 0) > 0) {
      this.markRead(groupId);
    }
  }

  // ---- SSE（由 DirectMessageStore 轉送） ----

  onMessage(message: GroupMessage): void {
    const myId = this.myId;
    if (!myId) return;
    const isNew = !this.messages().some((m) => m.id === message.id);
    const openGroupId = this.visible() ? this.openGroupId() : null;
    const known = this.groups().some((g) => g.id === message.groupId);
    this.groups.update((list) => applyGroupMessage(list, message, myId, openGroupId, isNew));
    if (!known) this.refresh();
    if (this.openGroupId() === message.groupId) {
      this.messages.update((list) => mergeGroupMessages(list, [message]));
      this.followBot(message);
      if (isNew && this.visible() && message.senderId !== myId) this.markRead(message.groupId);
    }
  }

  onUpdated(update: GroupUpdated): void {
    if (update.removed) {
      this.dropGroup(update.groupId);
      return;
    }

    this.api.listChatGroups().subscribe((groups) => this.groups.set(sortGroups(groups)));
    if (this.openGroupId() === update.groupId) {
      this.api.getChatGroup(update.groupId).subscribe({
        next: (group) => this.applyDetail(group),
        error: () => this.dropGroup(update.groupId),
      });
    }
  }

  onRead(read: GroupRead): void {
    if (read.readerId === this.myId) {
      this.groups.update((list) => clearGroupUnread(list, read.groupId));
    }
  }

  private reloadOpen(): void {
    const groupId = this.openGroupId();
    if (!groupId) return;
    this.loading.set(true);
    this.api.getChatGroup(groupId).subscribe({
      next: (group) => this.applyDetail(group),
      error: () => this.dropGroup(groupId),
    });
    this.api.listGroupMessages(groupId).subscribe({
      next: (page) => {
        if (this.openGroupId() !== groupId) return;
        this.messages.update((list) => mergeGroupMessages(list, page.messages));
        this.hasMore.set(page.hasMore);
        this.loading.set(false);
        page.messages.forEach((m) => this.followBot(m));
        if (this.visible()) this.markRead(groupId);
      },
      error: (e: unknown) => {
        this.loading.set(false);
        this.error.set(describeApiError(e));
      },
    });
  }

  /** Pending 的 Bot 訊息：訂閱群組串流顯示即時回答；完成後由 group-message 事件帶回最後內容。 */
  private followBot(message: GroupMessage): void {
    const executionId = message.executionId;
    if (message.senderKind !== 'Bot' || !executionId) return;
    if (message.botStatus !== 'Pending') {
      this.stopLive(executionId);
      return;
    }

    if (this.liveSubscriptions.has(executionId)) return;
    this.live.update((map) => ({ ...map, [executionId]: initialExecutionView() }));
    const url = `/api/chat-groups/${message.groupId}/executions/${executionId}/events`;
    const subscription = this.streams.stream(url).subscribe({
      next: (event) =>
        this.live.update((map) => ({
          ...map,
          [executionId]: applyExecutionEvent(map[executionId] ?? initialExecutionView(), event),
        })),
      error: () => this.stopLive(executionId),
    });
    this.liveSubscriptions.set(executionId, subscription);
  }

  private stopLive(executionId: string): void {
    this.liveSubscriptions.get(executionId)?.unsubscribe();
    this.liveSubscriptions.delete(executionId);
    this.live.update((map) => {
      const rest = { ...map };
      delete rest[executionId];
      return rest;
    });
  }

  private stopAllLive(): void {
    for (const subscription of this.liveSubscriptions.values()) subscription.unsubscribe();
    this.liveSubscriptions.clear();
    this.live.set({});
  }

  private markRead(groupId: string): void {
    this.groups.update((list) => clearGroupUnread(list, groupId));
    this.api.markGroupRead(groupId).subscribe({ error: () => undefined });
  }

  private applyDetail(group: ChatGroup): void {
    if (this.openGroupId() !== group.id) return;
    this.detail.set(group);
    this.groups.update((list) =>
      list.map((g) =>
        g.id === group.id ? { ...g, name: group.name, memberCount: group.members.length } : g,
      ),
    );
  }

  private dropGroup(groupId: string): void {
    this.groups.update((list) => list.filter((g) => g.id !== groupId));
    if (this.openGroupId() === groupId) this.close();
  }

  private requireOpen(): string {
    const groupId = this.openGroupId();
    if (!groupId) throw new Error('沒有開啟的群組');
    return groupId;
  }
}
