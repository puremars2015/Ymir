import { Plugin } from '../api/api-types';

/** 外掛市集卡片上的狀態標籤（ADR-0030 §1）。 */
export function pluginStatusLabel(plugin: Plugin): { text: string; tone: 'ok' | 'muted' | 'warn' } {
  if (!plugin.allowed) {
    return { text: '管理員未開放', tone: 'muted' };
  }
  if (!plugin.installed) {
    return { text: '尚未安裝', tone: 'muted' };
  }
  if (!plugin.enabled) {
    return { text: '已安裝，未啟用', tone: 'muted' };
  }
  return plugin.agentReady
    ? { text: 'Agent 可以使用', tone: 'ok' }
    : { text: '已啟用，暫時無法使用', tone: 'warn' };
}

/** 啟用前要使用者確認的風險說明；不需要確認時回傳 null（ADR-0030 §5）。 */
export function enableConfirmation(plugin: Plugin): string | null {
  if (plugin.kind !== 'OneDrive') {
    return null;
  }
  return (
    '啟用後，Agent 可以讀取、修改、搬移與刪除你 OneDrive 上的任何檔案。\n' +
    '對話中的檔案或網頁內容可能誘導 Agent 做出你沒要求的操作；刪除的檔案會先放進 OneDrive 回收筒。\n\n' +
    '確定要讓 Agent 使用 OneDrive 嗎？'
  );
}

/** OneDrive 卡片排最前面，其餘依名稱排序。 */
export function sortPlugins(plugins: readonly Plugin[]): Plugin[] {
  return [...plugins].sort((a, b) =>
    a.kind === b.kind ? a.name.localeCompare(b.name) : a.kind === 'OneDrive' ? -1 : 1,
  );
}

/** 開關更新後以新的卡片取代清單中的同一項。 */
export function replacePlugin(plugins: readonly Plugin[], updated: Plugin): Plugin[] {
  return plugins.map((p) => (p.key === updated.key ? updated : p));
}
