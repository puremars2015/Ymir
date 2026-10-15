import { ChatGroupSummary, GroupMessage } from '../api/api-types';

/** 群組名稱上限，與後端 `ChatGroup.NameMaxLength` 一致（ADR-0026）。 */
export const GROUP_NAME_MAX_LENGTH = 100;

/** 群組成員上限（含擁有者），與後端 `ChatGroup.MaxMembers` 一致。 */
export const GROUP_MAX_MEMBERS = 50;

/** 與後端 `GroupPromptBuilder.ParseBotRequest` 相同：開頭是 @bot（不分大小寫），後面是空白或結束。 */
export function isBotCommand(text: string): boolean {
  return /^\s*@bot(\s|$)/i.test(text);
}

/** @bot 後面的內容（不是 @bot 指令時為 null）。 */
export function botRequestOf(text: string): string | null {
  const match = /^\s*@bot(\s|$)/i.exec(text);
  return match ? text.trimStart().slice(4).trim() : null;
}

/** 只附檔案、沒有輸入問題時送給 @bot 的內容。 */
export const GROUP_ATTACHMENT_PROMPT = '請看看我附加的檔案。';

/**
 * 送出的群組訊息文字。附件是給 Agent 的（後端只接受隨 @bot 送出），
 * 所以有附件時自動補上 @bot；沒有問題時用預設的說明。沒有可送出的內容時回傳 null。
 */
export function groupSubmissionText(text: string, attachmentCount: number): string | null {
  const trimmed = text.trim();
  if (attachmentCount === 0) return trimmed.length > 0 ? trimmed : null;
  const request = botRequestOf(trimmed) ?? trimmed;
  return `@bot ${request || GROUP_ATTACHMENT_PROMPT}`;
}

export function normalizeGroupName(name: string): string | null {
  const text = name.trim();
  return text.length === 0 || text.length > GROUP_NAME_MAX_LENGTH ? null : text;
}

/** 群組清單排序：有未讀的在前，再依最後訊息時間（與後端相同）。 */
export function sortGroups(groups: readonly ChatGroupSummary[]): ChatGroupSummary[] {
  return [...groups].sort((a, b) => {
    const unread = Number(Number(b.unreadCount) > 0) - Number(Number(a.unreadCount) > 0);
    if (unread !== 0) return unread;
    return (b.lastMessageAt ?? b.updatedAt).localeCompare(a.lastMessageAt ?? a.updatedAt);
  });
}

/** 依時間合併訊息並以 id 去重；後到的版本覆蓋先前的（例如 Bot 訊息從 Pending 更新為回答）。 */
export function mergeGroupMessages(
  existing: readonly GroupMessage[],
  incoming: readonly GroupMessage[],
): GroupMessage[] {
  const byId = new Map(existing.map((m) => [m.id, m]));
  for (const message of incoming) {
    byId.set(message.id, { ...byId.get(message.id), ...message });
  }
  return [...byId.values()].sort(
    (a, b) => a.createdAt.localeCompare(b.createdAt) || a.id.localeCompare(b.id),
  );
}

/**
 * 新訊息進來時更新群組清單：最後訊息時間；別人送的（含 Bot）在沒有開著那個群組時加未讀數。
 * Bot 訊息的更新（Pending → 回答）不重複計算未讀。
 */
export function applyGroupMessage(
  groups: readonly ChatGroupSummary[],
  message: GroupMessage,
  myId: string,
  openGroupId: string | null,
  isNew: boolean,
): ChatGroupSummary[] {
  const fromOther = message.senderKind === 'Bot' || message.senderId !== myId;
  const unread = isNew && fromOther && message.groupId !== openGroupId ? 1 : 0;
  return sortGroups(
    groups.map((g) =>
      g.id === message.groupId
        ? {
            ...g,
            unreadCount: Number(g.unreadCount) + unread,
            lastMessageAt:
              (g.lastMessageAt ?? '') > message.createdAt ? g.lastMessageAt : message.createdAt,
          }
        : g,
    ),
  );
}

export function clearGroupUnread(
  groups: readonly ChatGroupSummary[],
  groupId: string,
): ChatGroupSummary[] {
  return groups.map((g) => (g.id === groupId ? { ...g, unreadCount: 0 } : g));
}

export function totalGroupUnread(groups: readonly ChatGroupSummary[]): number {
  return groups.reduce((sum, g) => sum + Number(g.unreadCount), 0);
}

/** 訊息的寄件者顯示：Bot 訊息標示是回覆誰。 */
export function senderLabel(message: GroupMessage): string {
  if (message.senderKind === 'Bot') {
    return message.invokedByName ? `@bot · 回覆 ${message.invokedByName}` : '@bot';
  }
  return message.senderName ?? '成員';
}
