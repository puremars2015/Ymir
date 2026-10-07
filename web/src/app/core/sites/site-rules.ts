import { SiteAccessMode, WorkspaceFile } from '../api/api-types';

/** 可以發布成網站的目錄：工作目錄中含有 index.html 的目錄（`.` 表示工作目錄本身，ADR-0016 §2）。 */
export function publishableDirectories(files: readonly WorkspaceFile[]): string[] {
  const directories = files
    .map((f) => f.path)
    .filter((path) => path === 'index.html' || path.endsWith('/index.html'))
    .map((path) => (path === 'index.html' ? '.' : path.slice(0, -'/index.html'.length)));
  // 常見的建置輸出放前面。
  const rank = (dir: string) =>
    /(^|\/)(dist|build|out|public)$/.test(dir) ? 0 : dir === '.' ? 2 : 1;
  return [...new Set(directories)].sort((a, b) => rank(a) - rank(b) || a.localeCompare(b));
}

export function siteAccessLabel(mode: SiteAccessMode): string {
  switch (mode) {
    case 'Public':
      return '公開';
    case 'AllUsers':
      return '所有 Ymir 使用者';
    default:
      return '指定使用者';
  }
}
