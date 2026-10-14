import { describe, expect, it } from 'vitest';
import { DirectContact, DirectMessage } from '../api/api-types';
import {
  applyMessageToContacts,
  applyReadReceipt,
  clearUnread,
  filterContacts,
  formatMessageTime,
  mergeMessages,
  normalizeDirectMessage,
  sortContacts,
  totalUnread,
} from './direct-message-state';

const me = 'me';

function contact(userId: string, extra: Partial<DirectContact> = {}): DirectContact {
  return {
    userId,
    displayName: userId,
    accountName: null,
    department: null,
    unreadCount: 0,
    lastMessageAt: null,
    ...extra,
  };
}

function message(
  id: string,
  senderId: string,
  recipientId: string,
  createdAt: string,
): DirectMessage {
  return { id, senderId, recipientId, body: id, createdAt, readAt: null };
}

describe('direct message state', () => {
  it('sorts unread first, then by last message, then by name', () => {
    const sorted = sortContacts([
      contact('b'),
      contact('a'),
      contact('recent', { lastMessageAt: '2026-10-14T02:00:00Z' }),
      contact('unread', { unreadCount: 2, lastMessageAt: '2026-10-14T01:00:00Z' }),
    ]);
    expect(sorted.map((c) => c.userId)).toEqual(['unread', 'recent', 'a', 'b']);
  });

  it('merges messages by id in time order', () => {
    const first = message('1', me, 'peer', '2026-10-14T01:00:00Z');
    const second = message('2', 'peer', me, '2026-10-14T02:00:00Z');
    const merged = mergeMessages([second], [first, { ...second, readAt: '2026-10-14T03:00:00Z' }]);
    expect(merged.map((m) => m.id)).toEqual(['1', '2']);
    expect(merged[1].readAt).toBe('2026-10-14T03:00:00Z');
  });

  it('counts unread only for received messages from a peer that is not open', () => {
    const contacts = [contact('peer'), contact('other')];
    const incoming = message('1', 'peer', me, '2026-10-14T01:00:00Z');

    const closed = applyMessageToContacts(contacts, incoming, me, null);
    expect(closed[0]).toMatchObject({
      userId: 'peer',
      unreadCount: 1,
      lastMessageAt: incoming.createdAt,
    });

    const open = applyMessageToContacts(contacts, incoming, me, 'peer');
    expect(open[0]).toMatchObject({ userId: 'peer', unreadCount: 0 });

    const outgoing = applyMessageToContacts(
      contacts,
      message('2', me, 'other', '2026-10-14T02:00:00Z'),
      me,
      null,
    );
    expect(outgoing[0]).toMatchObject({ userId: 'other', unreadCount: 0 });
    expect(totalUnread(closed)).toBe(1);
    expect(totalUnread(clearUnread(closed, 'peer'))).toBe(0);
  });

  it('marks my messages as read when the peer reads them', () => {
    const mine = message('1', me, 'peer', '2026-10-14T01:00:00Z');
    const theirs = message('2', 'peer', me, '2026-10-14T02:00:00Z');
    const result = applyReadReceipt([mine, theirs], {
      readerId: 'peer',
      peerId: me,
      readAt: '2026-10-14T03:00:00Z',
      count: 1,
    });
    expect(result[0].readAt).toBe('2026-10-14T03:00:00Z');
    expect(result[1].readAt).toBeNull();
  });

  it('filters by name, account and department', () => {
    const contacts = [
      contact('1', { displayName: '王小明', department: '業務部' }),
      contact('2', { displayName: 'Alice', accountName: 'alice.chen' }),
    ];
    expect(filterContacts(contacts, '業務').map((c) => c.userId)).toEqual(['1']);
    expect(filterContacts(contacts, 'CHEN').map((c) => c.userId)).toEqual(['2']);
    expect(filterContacts(contacts, '  ')).toHaveLength(2);
  });

  it('normalizes the message text', () => {
    expect(normalizeDirectMessage('  hi  ')).toBe('hi');
    expect(normalizeDirectMessage('   ')).toBeNull();
    expect(normalizeDirectMessage('x'.repeat(4001))).toBeNull();
  });

  it('formats today as time only', () => {
    const now = new Date(2026, 9, 14, 15, 0);
    expect(formatMessageTime(new Date(2026, 9, 14, 9, 5).toISOString(), now)).toBe('09:05');
    expect(formatMessageTime(new Date(2026, 9, 13, 21, 30).toISOString(), now)).toBe('10/13 21:30');
  });
});
