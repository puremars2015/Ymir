import { renderMarkdown } from './markdown';

describe('renderMarkdown', () => {
  it('renders headings, lists, emphasis and inline code', () => {
    const html = renderMarkdown('## 啟用帳戶\n\n- 第一步 **重要**\n- 執行 `net user`');
    expect(html).toContain('<h2>啟用帳戶</h2>');
    expect(html).toContain('<li>第一步 <strong>重要</strong></li>');
    expect(html).toContain('<code>net user</code>');
  });

  it('renders fenced code blocks with the language class', () => {
    const html = renderMarkdown('```cmd\nnet user Administrator /active:yes\n```');
    expect(html).toContain('<pre><code class="language-cmd">net user Administrator /active:yes');
  });

  it('turns single newlines into line breaks and renders GFM tables', () => {
    expect(renderMarkdown('第一行\n第二行')).toContain('第一行<br>第二行');
    const table = renderMarkdown('| 名稱 | 值 |\n|---|---|\n| a | 1 |');
    expect(table).toContain('<table>');
    expect(table).toContain('<td>a</td>');
  });

  it('escapes raw HTML from the model', () => {
    const html = renderMarkdown('<script>alert(1)</script>\n\n文字 <img src=x onerror=alert(1)>');
    expect(html).not.toContain('<script');
    expect(html).not.toContain('<img');
    expect(html).toContain('&lt;script&gt;');
  });

  it('only links safe protocols and opens them in a new tab', () => {
    const safe = renderMarkdown('[文件](https://example.com/a)');
    expect(safe).toContain(
      '<a href="https://example.com/a" target="_blank" rel="noopener noreferrer">文件</a>',
    );
    const unsafe = renderMarkdown('[點我](javascript:alert(1))');
    expect(unsafe).not.toContain('<a');
    expect(unsafe).toContain('點我');
  });

  it('does not load images (shows them as links)', () => {
    const html = renderMarkdown('![圖](https://example.com/x.png)');
    expect(html).not.toContain('<img');
    expect(html).toContain('href="https://example.com/x.png"');
  });

  it('handles an unclosed code fence while streaming', () => {
    const html = renderMarkdown('說明：\n\n```bash\necho hi');
    expect(html).toContain('<pre><code class="language-bash">echo hi');
  });
});
