import { DirectContact, DirectMessage, DirectMessagesRead } from '../api/api-types';

/** 單則訊息上限，與後端 `DirectMessage.BodyMaxLength` 一致（ADR-0023）。 */
export const DIRECT_MESSAGE_MAX_LENGTH = 4000;

/** 名單排序：有未讀的在前，再依最近往來時間，最後依名稱（與後端相同）。 */
export function sortContacts(contacts: readonly DirectContact[]): DirectContact[] {
  return [...contacts].sort((a, b) => {
    const unread = Number(Number(b.unreadCount) > 0) - Number(Number(a.unreadCount) > 0);
    if (unread !== 0) return unread;
    const last = (b.lastMessageAt ?? '').localeCompare(a.lastMessageAt ?? '');
    if (last !== 0) return last;
    return a.displayName.localeCompare(b.displayName, 'zh-Hant');
  });
}

/** 依時間合併訊息並以 id 去重（SSE 與 HTTP 回應可能帶回同一則訊息）。後到的版本覆蓋先前的（例如已讀時間）。 */
export function mergeMessages(
  existing: readonly DirectMessage[],
  incoming: readonly DirectMessage[],
): DirectMessage[] {
  const byId = new Map(existing.map((m) => [m.id, m]));
  for (const message of incoming) {
    byId.set(message.id, { ...byId.get(message.id), ...message });
  }
  return [...byId.values()].sort(
    (a, b) => a.createdAt.localeCompare(b.createdAt) || a.id.localeCompare(b.id),
  );
}

/** 訊息的「對方」：自己送出的看收件者，收到的看寄件者。 */
export function peerOf(message: DirectMessage, myId: string): string {
  return message.senderId === myId ? message.recipientId : message.senderId;
}

/**
 * 新訊息進來時更新名單：最後往來時間；收到的訊息在沒有開著那個對話時加未讀數。
 * 對方不在名單上（例如名單正在重新讀取）時不處理，下次讀名單就會補上。
 */
export function applyMessageToContacts(
  contacts: readonly DirectContact[],
  message: DirectMessage,
  myId: string,
  openPeerId: string | null,
): DirectContact[] {
  const peerId = peerOf(message, myId);
  const unread = message.recipientId === myId && peerId !== openPeerId ? 1 : 0;
  return sortContacts(
    contacts.map((c) =>
      c.userId === peerId
        ? {
            ...c,
            unreadCount: Number(c.unreadCount) + unread,
            lastMessageAt:
              (c.lastMessageAt ?? '') > message.createdAt ? c.lastMessageAt : message.createdAt,
          }
        : c,
    ),
  );
}

/** 自己讀了某人的訊息（可能是在另一個分頁）：那個人的未讀數歸零。 */
export function clearUnread(contacts: readonly DirectContact[], peerId: string): DirectContact[] {
  return sortContacts(contacts.map((c) => (c.userId === peerId ? { ...c, unreadCount: 0 } : c)));
}

/** 對方讀了我的訊息：把我送給他、還沒標示已讀的訊息加上已讀時間。 */
export function applyReadReceipt(
  messages: readonly DirectMessage[],
  read: DirectMessagesRead,
): DirectMessage[] {
  return messages.map((m) =>
    m.senderId === read.peerId && m.recipientId === read.readerId && !m.readAt
      ? { ...m, readAt: read.readAt }
      : m,
  );
}

export function totalUnread(contacts: readonly DirectContact[]): number {
  return contacts.reduce((sum, c) => sum + Number(c.unreadCount), 0);
}

/** 名單搜尋（前端篩選）：名稱、帳號、部門，不分大小寫。 */
export function filterContacts(contacts: readonly DirectContact[], query: string): DirectContact[] {
  const term = query.trim().toLocaleLowerCase();
  if (!term) return [...contacts];
  return contacts.filter((c) =>
    [c.displayName, c.accountName, c.department].some((v) => v?.toLocaleLowerCase().includes(term)),
  );
}

/** 去掉前後空白；空白或超過上限時回傳 null（後端也會再檢查）。 */
export function normalizeDirectMessage(text: string): string | null {
  const trimmed = text.trim();
  return trimmed && trimmed.length <= DIRECT_MESSAGE_MAX_LENGTH ? trimmed : null;
}

/** 訊息時間：今天只顯示時間，其他日子加上月 / 日（以瀏覽器時區）。 */
export function formatMessageTime(iso: string, now: Date = new Date()): string {
  const date = new Date(iso);
  const time = `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`;
  const sameDay =
    date.getFullYear() === now.getFullYear() &&
    date.getMonth() === now.getMonth() &&
    date.getDate() === now.getDate();
  return sameDay ? time : `${date.getMonth() + 1}/${date.getDate()} ${time}`;
}
