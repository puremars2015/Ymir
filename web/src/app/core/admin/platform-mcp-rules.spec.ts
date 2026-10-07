import { mcpAccessSummary, toggleUser } from './platform-mcp-rules';

describe('platform mcp rules', () => {
  it('summarizes access', () => {
    expect(mcpAccessSummary({ enabled: false, mode: 'Everyone', userIds: [] })).toBe('停用');
    expect(mcpAccessSummary({ enabled: true, mode: 'Everyone', userIds: [] })).toBe(
      '所有使用者可用',
    );
    expect(mcpAccessSummary({ enabled: true, mode: 'AdminsOnly', userIds: [] })).toBe(
      '只有管理員可用',
    );
    expect(mcpAccessSummary({ enabled: true, mode: 'SelectedUsers', userIds: [] })).toBe(
      '尚未指定使用者',
    );
    expect(mcpAccessSummary({ enabled: true, mode: 'SelectedUsers', userIds: ['a', 'b'] })).toBe(
      '指定 2 位使用者',
    );
  });

  it('toggles users without duplicates', () => {
    expect(toggleUser(['a'], 'b', true)).toEqual(['a', 'b']);
    expect(toggleUser(['a', 'b'], 'a', true)).toEqual(['b', 'a']);
    expect(toggleUser(['a', 'b'], 'a', false)).toEqual(['b']);
  });
});
