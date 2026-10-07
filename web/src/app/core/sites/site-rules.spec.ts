import { WorkspaceFile } from '../api/api-types';
import {
  addShare,
  publishableDirectories,
  removeShare,
  siteAccessLabel,
  siteAccessTarget,
} from './site-rules';

const file = (path: string): WorkspaceFile => ({
  path,
  size: 1,
  modifiedAt: '2026-10-08T00:00:00Z',
});

describe('site rules', () => {
  it('finds directories with index.html, build outputs first', () => {
    expect(
      publishableDirectories([
        file('index.html'),
        file('docs/index.html'),
        file('web/dist/index.html'),
        file('web/dist/app.js'),
        file('README.md'),
      ]),
    ).toEqual(['web/dist', 'docs', '.']);
    expect(publishableDirectories([file('a.txt')])).toEqual([]);
  });

  it('labels access modes', () => {
    expect(siteAccessLabel('Public')).toBe('公開');
    expect(siteAccessLabel('SelectedUsers')).toBe('指定使用者');
  });

  it('keeps the share list unique', () => {
    const a = { userId: 'a', displayName: 'A', accountName: 'a@x' };
    const b = { userId: 'b', displayName: 'B', accountName: null };
    const list = addShare(addShare([], a), b);
    expect(addShare(list, a)).toEqual([a, b]);
    expect(removeShare(list, 'a')).toEqual([b]);
  });

  it('accepts only a site id and a site-relative return path', () => {
    const id = '01a11722-5219-7400-8000-000000000001';
    expect(siteAccessTarget(id, '/orders/1?x=1')).toEqual({ siteId: id, path: '/orders/1?x=1' });
    expect(siteAccessTarget(id, null)).toEqual({ siteId: id, path: '/' });
    expect(siteAccessTarget(id, '//evil.example/')).toEqual({ siteId: id, path: '/' });
    expect(siteAccessTarget(id, 'https://evil.example/')).toEqual({ siteId: id, path: '/' });
    expect(siteAccessTarget(id, '/a\\b')).toEqual({ siteId: id, path: '/' });
    expect(siteAccessTarget('../admin', '/')).toBeNull();
    expect(siteAccessTarget(null, '/')).toBeNull();
  });
});
