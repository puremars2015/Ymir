import { ExtensionGrantSetting, ExtensionValues, RestrictedNetworkSupport } from '../api/api-types';

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
  {
    key: 'oneDrive',
    label: 'OneDrive 連結',
    description:
      '讓使用者連結自己的 OneDrive，執行前後自動同步工作檔案；憑證只由平台保管，Agent 拿不到。',
  },
  {
    key: 'internet',
    label: '對外連線',
    description:
      'Agent 能否連到平台的模型與服務以外的位址（網際網路、內網）。關閉時無法下載套件或呼叫外部 API。',
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

/**
 * 關閉對外連線能否生效（ADR-0012 A.8）。null 表示已設定受限網路，不需要提醒。
 * 個人覆寫也可能關閉對外連線，所以不論全域預設為何都顯示。
 */
export function restrictedNetworkWarning(support: RestrictedNetworkSupport): string | null {
  switch (support) {
    case 'NotConfigured':
      return '尚未設定受限網路（VibeMaker__Runtime__RestrictedNetwork）：被關閉對外連線的成員將無法執行 Agent。';
    case 'Unknown':
      return '受限網路由 runtime host 設定；關閉前請確認已依部署文件建立並設定（deploy/runtime-host/README.md「受限網路」），否則被關閉的成員無法執行 Agent。';
    case 'NotEnforced':
      return '開發模式（Local runtime）沒有隔離，不會限制網路。';
    default:
      return null;
  }
}
