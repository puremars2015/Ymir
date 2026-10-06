import { WorkspaceFile } from '../api/api-types';

/** 下載單一檔案的網址（瀏覽器直接導覽下載，cookie 自動帶上，ADR-0002）。 */
export const fileDownloadUrl = (conversationId: string, path: string): string =>
  `/api/conversations/${conversationId}/files/download?path=${encodeURIComponent(path)}`;

export const archiveDownloadUrl = (conversationId: string): string =>
  `/api/conversations/${conversationId}/files/archive`;

/** 1023 B、1.5 KB、12 MB。 */
export function formatSize(value: number | string): string {
  const bytes = Number(value);
  if (!Number.isFinite(bytes) || bytes < 1024) return `${Math.max(0, Math.round(bytes || 0))} B`;
  const units = ['KB', 'MB', 'GB'];
  let size = bytes / 1024;
  let unit = 0;
  while (size >= 1024 && unit < units.length - 1) {
    size /= 1024;
    unit++;
  }
  return `${size >= 10 ? Math.round(size) : size.toFixed(1)} ${units[unit]}`;
}

export const fileName = (path: string): string => path.slice(path.lastIndexOf('/') + 1);

/** 子目錄（沒有則為空字串），列表上顯示在檔名下方。 */
export const fileDirectory = (path: string): string =>
  path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : '';

/**
 * 這一輪 Agent 新增或修改的檔案：與送出前的清單比較（大小或修改時間不同就算修改）。
 * 用伺服器回傳的值比較，不依賴瀏覽器時鐘。
 */
export function changedFiles(before: WorkspaceFile[], after: WorkspaceFile[]): WorkspaceFile[] {
  const previous = new Map(before.map((f) => [f.path, f]));
  return after.filter((file) => {
    const old = previous.get(file.path);
    return !old || String(old.size) !== String(file.size) || old.modifiedAt !== file.modifiedAt;
  });
}

const ICONS: Record<string, string> = {
  html: '🌐',
  htm: '🌐',
  css: '🎨',
  js: '📜',
  ts: '📜',
  json: '🧾',
  md: '📝',
  txt: '📝',
  py: '🐍',
  png: '🖼',
  jpg: '🖼',
  jpeg: '🖼',
  gif: '🖼',
  svg: '🖼',
  zip: '🗜',
  csv: '📊',
  xlsx: '📊',
  pdf: '📕',
};

export function fileIcon(path: string): string {
  const dot = path.lastIndexOf('.');
  return (dot >= 0 && ICONS[path.slice(dot + 1).toLowerCase()]) || '📄';
}
