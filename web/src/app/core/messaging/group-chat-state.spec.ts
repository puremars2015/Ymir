import { ChatGroupSummary, GroupMessage } from '../api/api-types';
import {
  applyGroupMessage,
  botRequestOf,
  clearGroupUnread,
  GROUP_ATTACHMENT_PROMPT,
  groupSubmissionText,
  isBotCommand,
  mergeGroupMessages,
  normalizeGroupName,
  senderLabel,
  sortGroups,
  totalGroupUnread,
} from './group-chat-state';

const group = (patch: Partial<ChatGroupSummary>): ChatGroupSummary => ({
  id: 'g1',
  name: '小組',
  ownerId: 'u1',
  role: 'Member',
  memberCount: 3,
  unreadCount: 0,
  lastMessageAt: null,
  updatedAt: '2026-10-15T00:00:00Z',
  ...patch,
});

const message = (patch: Partial<GroupMessage>): GroupMessage => ({
  id: 'm1',
  groupId: 'g1',
  senderKind: 'User',
  senderId: 'u2',
  senderName: 'Bob',
  body: '嗨',
  createdAt: '2026-10-15T01:00:00Z',
  executionId: null,
  invokedById: null,
  invokedByName: null,
  botStatus: null,
  ...patch,
});

describe('group-chat-state', () => {
  it('recognises @bot like the server does', () => {
    expect(isBotCommand('@bot 你好')).toBe(true);
    expect(isBotCommand('  @BOT\n整理')).toBe(true);
    expect(isBotCommand('@botty 你好')).toBe(false);
    expect(isBotCommand('請 @bot 幫忙')).toBe(false);
    expect(botRequestOf('@bot  整理重點 ')).toBe('整理重點');
    expect(botRequestOf('@bot')).toBe('');
    expect(botRequestOf('hello')).toBeNull();
  });

  it('validates group names', () => {
    expect(normalizeGroupName('  專案 ')).toBe('專案');
    expect(normalizeGroupName('   ')).toBeNull();
    expect(normalizeGroupName('x'.repeat(101))).toBeNull();
  });

  it('counts unread from others and bots only for new messages in closed groups', () => {
    const groups = [group({})];
    expect(applyGroupMessage(groups, message({}), 'u1', null, true)[0].unreadCount).toBe(1);
    expect(
      applyGroupMessage(groups, message({ senderId: 'u1' }), 'u1', null, true)[0].unreadCount,
    ).toBe(0);
    expect(applyGroupMessage(groups, message({}), 'u1', 'g1', true)[0].unreadCount).toBe(0);
    const bot = message({ senderKind: 'Bot', senderId: null, botStatus: 'Completed' });
    expect(applyGroupMessage(groups, bot, 'u1', null, true)[0].unreadCount).toBe(1);
    expect(applyGroupMessage(groups, bot, 'u1', null, false)[0].unreadCount).toBe(0);
  });

  it('sorts unread first, merges updates and totals unread', () => {
    const sorted = sortGroups([
      group({ id: 'a', lastMessageAt: '2026-10-15T03:00:00Z' }),
      group({ id: 'b', unreadCount: 2, lastMessageAt: '2026-10-15T01:00:00Z' }),
    ]);
    expect(sorted.map((g) => g.id)).toEqual(['b', 'a']);
    expect(totalGroupUnread(sorted)).toBe(2);
    expect(totalGroupUnread(clearGroupUnread(sorted, 'b'))).toBe(0);

    const pending = message({
      id: 'bot',
      senderKind: 'Bot',
      body: '',
      botStatus: 'Pending',
      createdAt: '2026-10-15T02:00:00Z',
    });
    const merged = mergeGroupMessages(
      [message({}), pending],
      [{ ...pending, body: '回答', botStatus: 'Completed' }],
    );
    expect(merged).toHaveLength(2);
    expect(merged[1].body).toBe('回答');
  });

  it('labels bot replies with the caller', () => {
    expect(senderLabel(message({}))).toBe('Bob');
    expect(senderLabel(message({ senderKind: 'Bot', invokedByName: 'Alice' }))).toBe(
      '@bot · 回覆 Alice',
    );
  });
});

describe('groupSubmissionText', () => {
  it('keeps plain messages without attachments', () => {
    expect(groupSubmissionText('  大家好 ', 0)).toBe('大家好');
    expect(groupSubmissionText('   ', 0)).toBeNull();
  });

  it('turns messages with attachments into @bot requests', () => {
    expect(groupSubmissionText('幫我看這份', 1)).toBe('@bot 幫我看這份');
    expect(groupSubmissionText('@BOT 幫我看這份', 2)).toBe('@bot 幫我看這份');
    expect(groupSubmissionText('', 1)).toBe(`@bot ${GROUP_ATTACHMENT_PROMPT}`);
    expect(groupSubmissionText('@bot', 1)).toBe(`@bot ${GROUP_ATTACHMENT_PROMPT}`);
  });
});
