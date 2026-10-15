import { toolFrameUrl } from './tool-rules';

describe('tool rules', () => {
  const ymir = 'https://ymir.example.com';

  it('accepts site-domain urls', () => {
    expect(
      toolFrameUrl('https://abcd2345.sites.example.com/.ymir/auth?ticket=x&path=%2F', ymir),
    ).toBe('https://abcd2345.sites.example.com/.ymir/auth?ticket=x&path=%2F');
    expect(toolFrameUrl('http://abcd2345.sites.localhost:5300/', 'http://localhost:4200')).toBe(
      'http://abcd2345.sites.localhost:5300/',
    );
  });

  it('rejects missing, relative, non-http and same-origin urls', () => {
    expect(toolFrameUrl(null, ymir)).toBeNull();
    expect(toolFrameUrl('/c/123', ymir)).toBeNull();
    expect(toolFrameUrl('javascript:alert(1)', ymir)).toBeNull();
    expect(toolFrameUrl('data:text/html,<script>1</script>', ymir)).toBeNull();
    expect(toolFrameUrl('https://ymir.example.com/evil', ymir)).toBeNull();
  });
});
