import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiService, describeApiError } from '../api/api.service';
import { Notification } from '../api/api-types';
import { UserEventStream } from '../events/user-event-stream';
import {
  addNotification,
  isViewingTarget,
  markNotificationsRead,
  mergeNotifications,
  NotificationsRead,
  safeNotificationLink,
  shouldShowBrowserNotification,
} from './notification-state';

/**
 * 站內通知（ADR-0032 N1）：頂列鈴鐺的清單與未讀數，以及分頁在背景時的瀏覽器通知。
 * 即時事件經 `/api/events`；每次連上（ready）重新讀取第一頁補齊斷線期間的通知。
 * 通知內容只以文字綁定顯示，連結只接受站內相對路徑。
 */
@Injectable({ providedIn: 'root' })
export class NotificationStore {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly stream = inject(UserEventStream);
  private unsubscribe: (() => void)[] = [];

  readonly items = signal<Notification[]>([]);
  readonly unreadCount = signal(0);
  readonly hasMore = signal(false);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  /** 瀏覽器通知的授權狀態；不支援時為 'unsupported'。 */
  readonly permission = signal<string>(
    typeof globalThis.Notification === 'undefined'
      ? 'unsupported'
      : globalThis.Notification.permission,
  );
  readonly canAskPermission = computed(() => this.permission() === 'default');

  /** 登入後呼叫；重複呼叫無作用。 */
  connect(): void {
    if (this.unsubscribe.length) return;
    this.unsubscribe = [
      this.stream.on('ready', () => this.refresh()),
      this.stream.on('notification', (data) => this.onCreated(JSON.parse(data) as Notification)),
      this.stream.on('notification-read', (data) =>
        this.onRead(JSON.parse(data) as NotificationsRead),
      ),
    ];
    if (!this.stream.supported) this.refresh();
  }

  /** 登出時取消訂閱並清掉資料。 */
  disconnect(): void {
    for (const off of this.unsubscribe) off();
    this.unsubscribe = [];
    this.items.set([]);
    this.unreadCount.set(0);
    this.hasMore.set(false);
    this.error.set(null);
  }

  refresh(): void {
    this.api.listNotifications().subscribe({
      next: (page) => {
        this.items.update((list) => mergeNotifications(list, page.items));
        this.hasMore.set(page.hasMore);
        this.unreadCount.set(Number(page.unreadCount));
      },
      error: (e: unknown) => this.error.set(describeApiError(e)),
    });
  }

  loadOlder(): void {
    const last = this.items().at(-1);
    if (!last || this.loading()) return;
    this.loading.set(true);
    this.api.listNotifications(last.id).subscribe({
      next: (page) => {
        this.items.update((list) => mergeNotifications(list, page.items));
        this.hasMore.set(page.hasMore);
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.loading.set(false);
        this.error.set(describeApiError(e));
      },
    });
  }

  /** 點選通知：標為已讀並前往站內連結（沒有連結時只標已讀）。 */
  open(item: Notification): void {
    if (!item.readAt) this.markRead(item.id);
    const link = safeNotificationLink(item.link);
    if (link) void this.router.navigateByUrl(link);
  }

  markRead(id: string): void {
    this.items.update((list) => markNotificationsRead(list, id, new Date().toISOString()));
    this.api.markNotificationRead(id).subscribe({
      next: (result) => this.unreadCount.set(Number(result.unreadCount)),
      error: () => undefined,
    });
  }

  markAllRead(): void {
    this.items.update((list) => markNotificationsRead(list, null, new Date().toISOString()));
    this.unreadCount.set(0);
    this.api.markAllNotificationsRead().subscribe({ error: () => undefined });
  }

  /** 使用者按下「啟用瀏覽器通知」時才詢問授權（瀏覽器要求由使用者動作觸發）。 */
  requestPermission(): void {
    const api = globalThis.Notification;
    if (typeof api === 'undefined') return;
    void api.requestPermission().then((result) => this.permission.set(result));
  }

  private onCreated(item: Notification): void {
    const hidden = globalThis.document?.visibilityState === 'hidden';
    if (isViewingTarget(item, { hidden, currentUrl: this.router.url })) {
      // 正看著同一個對話：結果已經在畫面上，直接標為已讀。
      this.items.update((list) => addNotification(list, item));
      this.markRead(item.id);
      return;
    }

    this.items.update((list) => addNotification(list, item));
    this.unreadCount.update((count) => count + 1);
    if (shouldShowBrowserNotification({ hidden, permission: this.permission() })) {
      this.showBrowserNotification(item);
    }
  }

  private onRead(read: NotificationsRead): void {
    this.items.update((list) => markNotificationsRead(list, read.id, new Date().toISOString()));
    this.unreadCount.set(Number(read.unreadCount));
  }

  private showBrowserNotification(item: Notification): void {
    try {
      // 內容只有伺服器產生的摘要；tag 讓同一則不會重複跳出（多個分頁）。
      const shown = new globalThis.Notification(item.title, {
        body: item.body ?? undefined,
        tag: item.id,
      });
      shown.onclick = () => {
        globalThis.focus?.();
        this.open(item);
        shown.close();
      };
    } catch {
      // 部分行動瀏覽器只允許經 service worker 顯示通知（Web Push 在 N4）；站內鈴鐺仍有這則通知。
    }
  }
}
