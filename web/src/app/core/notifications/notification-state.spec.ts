import { Notification } from '../api/api-types';
import {
  addNotification,
  isViewingTarget,
  markNotificationsRead,
  mergeNotifications,
  safeNotificationLink,
  shouldShowBrowserNotification,
} from './notification-state';

const item = (
  id: string,
  createdAt: string,
  overrides: Partial<Notification> = {},
): Notification => ({
  id,
  kind: 'ExecutionCompleted',
  title: `通知 ${id}`,
  body: null,
  link: `/c/${id}`,
  createdAt,
  readAt: null,
  ...overrides,
});

describe('notification state', () => {
  it('新通知放最前面且不重複', () => {
    const list = [item('a', '2026-10-29T01:00:00Z')];
    const added = addNotification(list, item('b', '2026-10-29T02:00:00Z'));
    expect(added.map((n) => n.id)).toEqual(['b', 'a']);
    expect(addNotification(added, item('b', '2026-10-29T02:00:00Z'))).toBe(added);
  });

  it('合併分頁後依時間由新到舊', () => {
    const merged = mergeNotifications(
      [item('b', '2026-10-29T02:00:00Z')],
      [item('a', '2026-10-29T01:00:00Z'), item('c', '2026-10-29T03:00:00Z')],
    );
    expect(merged.map((n) => n.id)).toEqual(['c', 'b', 'a']);
  });

  it('標為已讀：單則或全部，已讀的保留原時間', () => {
    const list = [
      item('a', '2026-10-29T01:00:00Z'),
      item('b', '2026-10-29T02:00:00Z', { readAt: '2026-10-29T02:05:00Z' }),
    ];
    expect(markNotificationsRead(list, 'a', 'now')[0].readAt).toBe('now');
    const all = markNotificationsRead(list, null, 'now');
    expect(all.map((n) => n.readAt)).toEqual(['now', '2026-10-29T02:05:00Z']);
  });

  it('只接受站內相對連結', () => {
    expect(safeNotificationLink('/c/abc')).toBe('/c/abc');
    expect(safeNotificationLink('//evil.example')).toBeNull();
    expect(safeNotificationLink('/\\evil.example')).toBeNull();
    expect(safeNotificationLink('https://evil.example')).toBeNull();
    expect(safeNotificationLink('javascript:alert(1)')).toBeNull();
    expect(safeNotificationLink('/c/a\nb')).toBeNull();
    expect(safeNotificationLink(null)).toBeNull();
  });

  it('分頁在背景且已授權才跳瀏覽器通知', () => {
    expect(shouldShowBrowserNotification({ hidden: true, permission: 'granted' })).toBe(true);
    expect(shouldShowBrowserNotification({ hidden: false, permission: 'granted' })).toBe(false);
    expect(shouldShowBrowserNotification({ hidden: true, permission: 'default' })).toBe(false);
  });

  it('正看著同一個對話時視為已讀', () => {
    const n = item('a', '2026-10-29T01:00:00Z');
    expect(isViewingTarget(n, { hidden: false, currentUrl: '/c/a?x=1' })).toBe(true);
    expect(isViewingTarget(n, { hidden: true, currentUrl: '/c/a' })).toBe(false);
    expect(isViewingTarget(n, { hidden: false, currentUrl: '/c/b' })).toBe(false);
    expect(isViewingTarget({ ...n, link: null }, { hidden: false, currentUrl: '/c/a' })).toBe(
      false,
    );
  });
});
