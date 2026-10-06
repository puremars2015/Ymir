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

export type PreviewKind = 'text' | 'markdown' | 'image' | 'too-large' | 'unsupported';

/** 文字預覽的上限；更大的檔案請下載。 */
export const MAX_TEXT_PREVIEW_BYTES = 512 * 1024;
export const MAX_IMAGE_PREVIEW_BYTES = 10 * 1024 * 1024;

const TEXT_EXTENSIONS = new Set([
  'txt',
  'log',
  'html',
  'htm',
  'css',
  'scss',
  'js',
  'mjs',
  'cjs',
  'ts',
  'tsx',
  'jsx',
  'json',
  'py',
  'csv',
  'tsv',
  'xml',
  'yml',
  'yaml',
  'toml',
  'ini',
  'sh',
  'sql',
  'cs',
  'java',
  'go',
  'rs',
  'rb',
  'php',
  'c',
  'h',
  'cpp',
  'vue',
  'svelte',
]);
const TEXT_NAMES = new Set(['dockerfile', 'containerfile', 'makefile', 'readme', 'license']);
const IMAGE_EXTENSIONS = new Set(['png', 'jpg', 'jpeg', 'gif', 'webp', 'svg']);

/**
 * 檔案面板的預覽方式。文字一律以文字綁定顯示（HTML 只看原始碼、不執行），
 * 圖片以 blob URL 放進 <img>（不會執行 SVG 內的腳本）。
 */
export function previewKind(path: string, size: number | string): PreviewKind {
  const name = fileName(path).toLowerCase();
  const dot = name.lastIndexOf('.');
  const extension = dot > 0 ? name.slice(dot + 1) : '';
  const bytes = Number(size);
  if (IMAGE_EXTENSIONS.has(extension)) {
    return bytes <= MAX_IMAGE_PREVIEW_BYTES ? 'image' : 'too-large';
  }
  const kind: PreviewKind | null =
    extension === 'md' || extension === 'markdown'
      ? 'markdown'
      : TEXT_EXTENSIONS.has(extension) || (!extension && TEXT_NAMES.has(name))
        ? 'text'
        : null;
  if (!kind) {
    return 'unsupported';
  }
  return bytes <= MAX_TEXT_PREVIEW_BYTES ? kind : 'too-large';
}
