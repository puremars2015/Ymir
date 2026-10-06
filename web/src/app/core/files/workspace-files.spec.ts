import { WorkspaceFile } from '../api/api-types';
import {
  archiveDownloadUrl,
  changedFiles,
  fileDirectory,
  fileDownloadUrl,
  fileIcon,
  fileName,
  formatSize,
  previewKind,
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

describe('previewKind', () => {
  it('文字與程式碼以原始碼預覽', () => {
    expect(previewKind('index.html', 1200)).toBe('text');
    expect(previewKind('src/app.ts', '300')).toBe('text');
    expect(previewKind('Dockerfile', 50)).toBe('text');
  });

  it('Markdown 以排版後的內容預覽', () => {
    expect(previewKind('docs/README.md', 10)).toBe('markdown');
  });

  it('圖片以 <img> 預覽', () => {
    expect(previewKind('logo.SVG', 2048)).toBe('image');
    expect(previewKind('a/b/photo.jpeg', 1024)).toBe('image');
  });

  it('太大的檔案請使用者下載', () => {
    expect(previewKind('data.json', 512 * 1024 + 1)).toBe('too-large');
    expect(previewKind('big.png', 11 * 1024 * 1024)).toBe('too-large');
  });

  it('其他類型無法預覽', () => {
    expect(previewKind('archive.zip', 10)).toBe('unsupported');
    expect(previewKind('report.pdf', 10)).toBe('unsupported');
    expect(previewKind('noextension', 10)).toBe('unsupported');
  });
});
