import { WorkspaceFile } from '../api/api-types';
import { publishableDirectories, siteAccessLabel } from './site-rules';

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
});
