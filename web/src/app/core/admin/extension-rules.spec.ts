import {
  capabilitySummary,
  effectiveValue,
  grantLabel,
  restrictedNetworkWarning,
} from './extension-rules';

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
    expect(capabilitySummary({ skills: true, mcp: false, internet: true })).toBe(
      '自建 skill：允許・自建 MCP server：不允許・對外連線：允許',
    );
  });

  it('warns unless the restricted network is configured', () => {
    expect(restrictedNetworkWarning('Configured')).toBeNull();
    expect(restrictedNetworkWarning('NotConfigured')).toContain('無法執行 Agent');
    expect(restrictedNetworkWarning('Unknown')).toContain('runtime host');
    expect(restrictedNetworkWarning('NotEnforced')).toContain('不會限制網路');
  });
});
