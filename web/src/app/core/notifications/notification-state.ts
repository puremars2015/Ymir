import { Notification } from '../api/api-types';

/**
 * SSE `notification-read` 的內容（ADR-0032 §2）。只出現在 `/api/events` 串流中、不在 OpenAPI，
 * 所以手寫；與後端 `NotificationsReadResponse` 同步修改。`id` 為 null 表示全部已讀。
 */
export interface NotificationsRead {
  id: string | null;
  unreadCount: number;
}

/** 新通知放最前面；同一則重複收到（重新連線補讀）時不重複。 */
export function addNotification(list: Notification[], item: Notification): Notification[] {
  return list.some((n) => n.id === item.id) ? list : [item, ...list];
}

/** 合併伺服器讀回的一頁（由新到舊），以 id 去重，保持由新到舊。 */
export function mergeNotifications(list: Notification[], page: Notification[]): Notification[] {
  const byId = new Map(list.map((n) => [n.id, n]));
  for (const item of page) byId.set(item.id, item);
  return [...byId.values()].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
}

/** 標為已讀；`id` 為 null 表示全部。 */
export function markNotificationsRead(
  list: Notification[],
  id: string | null,
  readAt: string,
): Notification[] {
  return list.map((n) => ((id === null || n.id === id) && !n.readAt ? { ...n, readAt } : n));
}

/**
 * 只接受站內相對路徑（以單一 `/` 開頭，不是 `//` 或 `/\`）。連結本來就只由伺服器產生（ADR-0032 §1），
 * 前端仍再檢查一次，避免開到其他網域。
 */
export function safeNotificationLink(link: string | null | undefined): string | null {
  if (!link || link[0] !== '/' || link[1] === '/' || link[1] === '\\') return null;
  // eslint-disable-next-line no-control-regex
  return /[\u0000-\u001f\u007f]/.test(link) ? null : link;
}

/** 是否另外跳出瀏覽器通知：分頁在背景（使用者沒在看 Ymir）而且已授權（ADR-0032 §2）。 */
export function shouldShowBrowserNotification(state: {
  hidden: boolean;
  permission: string;
}): boolean {
  return state.hidden && state.permission === 'granted';
}

/** 使用者正看著通知指向的對話（分頁在前景、網址相同）：直接標為已讀，不再提醒。 */
export function isViewingTarget(
  item: Notification,
  state: { hidden: boolean; currentUrl: string },
): boolean {
  const link = safeNotificationLink(item.link);
  return !state.hidden && link !== null && state.currentUrl.split(/[?#]/)[0] === link;
}
