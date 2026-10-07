import { ExtensionGrantSetting, ExtensionValues } from '../api/api-types';

/** 管理員可控制的擴充能力（ADR-0012 A.1）；顯示順序固定。 */
export const CAPABILITIES = [
  {
    key: 'skills',
    label: '自建 skill',
    description:
      '讓 Agent 依照使用者寫的指示（SKILL.md）工作；腳本仍只在該使用者自己的執行環境執行。',
  },
  {
    key: 'mcp',
    label: '自建 MCP server',
    description:
      '讓使用者加入自己的工具與連線目的地（例如外部 API）；憑證只能是使用者自己的，連不到平台後端。',
  },
] as const satisfies readonly { key: keyof ExtensionValues; label: string; description: string }[];

export type CapabilityKey = (typeof CAPABILITIES)[number]['key'];

export const GRANT_SETTINGS: readonly ExtensionGrantSetting[] = ['Inherit', 'Allow', 'Deny'];

export function allowedLabel(allowed: boolean): string {
  return allowed ? '允許' : '不允許';
}

/** 每人覆寫的選項文字；「繼承」附上目前全域預設的結果，避免管理員要另外查。 */
export function grantLabel(setting: ExtensionGrantSetting, defaultAllowed: boolean): string {
  switch (setting) {
    case 'Allow':
      return '允許';
    case 'Deny':
      return '不允許';
    default:
      return `依全域預設（${allowedLabel(defaultAllowed)}）`;
  }
}

/** 一行摘要，例如「自建 skill：允許・自建 MCP server：不允許」。 */
export function capabilitySummary(values: ExtensionValues): string {
  return CAPABILITIES.map((c) => `${c.label}：${allowedLabel(values[c.key])}`).join('・');
}

/** 套用覆寫後的有效值（與伺服器的規則相同，用於存檔前預覽）。 */
export function effectiveValue(setting: ExtensionGrantSetting, defaultAllowed: boolean): boolean {
  return setting === 'Inherit' ? defaultAllowed : setting === 'Allow';
}
