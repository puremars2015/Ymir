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

export const siteAccessModes: readonly SiteAccessMode[] = ['Public', 'AllUsers', 'SelectedUsers'];

export function siteAccessHint(mode: SiteAccessMode): string {
  switch (mode) {
    case 'Public':
      return '任何知道網址的人都能看，不需要登入。';
    case 'AllUsers':
      return '登入 Ymir 的使用者才能看。';
    default:
      return '只有你選擇的使用者能看。';
  }
}

/** 分享名單中的一位使用者（API 的 SiteShare 與搜尋結果共用的欄位）。 */
export interface ShareTarget {
  userId: string;
  displayName: string;
  accountName: string | null;
}

/** 加入分享名單（重複的忽略）。 */
export function addShare(list: readonly ShareTarget[], user: ShareTarget): ShareTarget[] {
  return list.some((s) => s.userId === user.userId) ? [...list] : [...list, user];
}

export function removeShare(list: readonly ShareTarget[], userId: string): ShareTarget[] {
  return list.filter((s) => s.userId !== userId);
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * `/site-access?site=&path=`（SiteHost 導過來，ADR-0016 §4）：網站 id 必須是 GUID；
 * 路徑只接受站內相對路徑，其他一律改成 `/`（後端也會再檢查一次）。
 */
export function siteAccessTarget(
  site: string | null,
  path: string | null,
): { siteId: string; path: string } | null {
  if (!site || !guidPattern.test(site)) {
    return null;
  }
  const safe =
    path &&
    path.length <= 2000 &&
    path.startsWith('/') &&
    !path.startsWith('//') &&
    !path.includes('\\') &&
    // eslint-disable-next-line no-control-regex
    !/[\u0000-\u001f\u007f]/.test(path)
      ? path
      : '/';
  return { siteId: site, path: safe };
}
