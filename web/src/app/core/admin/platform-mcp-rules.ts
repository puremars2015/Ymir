import { McpAccessMode, McpServerAccess } from '../api/api-types';

export const MCP_ACCESS_MODES: readonly { value: McpAccessMode; label: string }[] = [
  { value: 'Everyone', label: '所有使用者' },
  { value: 'AdminsOnly', label: '只有管理員' },
  { value: 'SelectedUsers', label: '指定使用者' },
];

/** 平台 MCP 服務的存取摘要（ADR-0012 B.4）：停用、所有人、管理員、或指定人數。 */
export function mcpAccessSummary(
  server: Pick<McpServerAccess, 'enabled' | 'mode' | 'userIds'>,
): string {
  if (!server.enabled) {
    return '停用';
  }

  switch (server.mode) {
    case 'Everyone':
      return '所有使用者可用';
    case 'AdminsOnly':
      return '只有管理員可用';
    default:
      return server.userIds.length === 0
        ? '尚未指定使用者'
        : `指定 ${server.userIds.length} 位使用者`;
  }
}

/** 切換指定使用者（勾選 / 取消）；回傳新的陣列，不修改原本的。 */
export function toggleUser(
  userIds: readonly string[],
  userId: string,
  selected: boolean,
): string[] {
  const without = userIds.filter((id) => id !== userId);
  return selected ? [...without, userId] : without;
}
