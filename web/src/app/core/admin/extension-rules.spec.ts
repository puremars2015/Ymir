import { capabilitySummary, effectiveValue, grantLabel } from './extension-rules';

describe('extension rules', () => {
  it('shows the inherited default next to the inherit option', () => {
    expect(grantLabel('Inherit', true)).toBe('依全域預設（允許）');
    expect(grantLabel('Inherit', false)).toBe('依全域預設（不允許）');
    expect(grantLabel('Allow', false)).toBe('允許');
    expect(grantLabel('Deny', true)).toBe('不允許');
  });

  it('resolves overrides like the server', () => {
    expect(effectiveValue('Inherit', true)).toBe(true);
    expect(effectiveValue('Inherit', false)).toBe(false);
    expect(effectiveValue('Allow', false)).toBe(true);
    expect(effectiveValue('Deny', true)).toBe(false);
  });

  it('summarizes every capability in a fixed order', () => {
    expect(capabilitySummary({ skills: true, mcp: false })).toBe(
      '自建 skill：允許・自建 MCP server：不允許',
    );
  });
});
