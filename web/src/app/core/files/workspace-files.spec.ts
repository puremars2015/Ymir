import { WorkspaceFile } from '../api/api-types';
import {
  archiveDownloadUrl,
  changedFiles,
  fileDirectory,
  fileDownloadUrl,
  fileIcon,
  fileName,
  formatSize,
} from './workspace-files';

const file = (path: string, size = 1, modifiedAt = '2026-10-06T00:00:00Z'): WorkspaceFile => ({
  path,
  size,
  modifiedAt,
});

describe('workspace files', () => {
  it('builds download urls with an encoded path', () => {
    expect(fileDownloadUrl('c1', 'src/中文 1.html')).toBe(
      '/api/conversations/c1/files/download?path=src%2F%E4%B8%AD%E6%96%87%201.html',
    );
    expect(archiveDownloadUrl('c1')).toBe('/api/conversations/c1/files/archive');
  });

  it('formats sizes', () => {
    expect(formatSize(0)).toBe('0 B');
    expect(formatSize(1023)).toBe('1023 B');
    expect(formatSize(1536)).toBe('1.5 KB');
    expect(formatSize('12582912')).toBe('12 MB');
  });

  it('splits names and directories', () => {
    expect(fileName('a/b/c.txt')).toBe('c.txt');
    expect(fileDirectory('a/b/c.txt')).toBe('a/b');
    expect(fileDirectory('c.txt')).toBe('');
  });

  it('finds files added or modified in this turn', () => {
    const before = [file('a.html'), file('b.css', 10)];
    const after = [
      file('a.html'),
      file('b.css', 12),
      file('c.js'),
      file('a2.html', 1, '2026-10-06T01:00:00Z'),
    ];
    expect(changedFiles(before, after).map((f) => f.path)).toEqual(['b.css', 'c.js', 'a2.html']);
    expect(changedFiles(after, after)).toEqual([]);
  });

  it('picks an icon by extension', () => {
    expect(fileIcon('calculator.HTML')).toBe('🌐');
    expect(fileIcon('README')).toBe('📄');
  });
});
