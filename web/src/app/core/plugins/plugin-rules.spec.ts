import { describe, expect, it } from 'vitest';
import { Plugin } from '../api/api-types';
import { enableConfirmation, pluginStatusLabel, replacePlugin, sortPlugins } from './plugin-rules';

const onedrive: Plugin = {
  key: 'onedrive',
  kind: 'OneDrive',
  name: 'OneDrive',
  description: '',
  allowed: true,
  installed: true,
  enabled: true,
  agentReady: true,
  notice: null,
};

const docs: Plugin = { ...onedrive, key: 'mcp:docs', kind: 'PlatformMcp', name: 'docs' };

describe('plugin rules', () => {
  it('labels each state, admin restrictions first', () => {
    expect(pluginStatusLabel({ ...onedrive, allowed: false }).text).toBe('管理員未開放');
    expect(pluginStatusLabel({ ...onedrive, installed: false }).text).toBe('尚未安裝');
    expect(pluginStatusLabel({ ...onedrive, enabled: false }).text).toBe('已安裝，未啟用');
    expect(pluginStatusLabel({ ...onedrive, agentReady: false })).toEqual({
      text: '已啟用，暫時無法使用',
      tone: 'warn',
    });
    expect(pluginStatusLabel(onedrive).tone).toBe('ok');
  });

  it('asks for confirmation only before giving the Agent OneDrive', () => {
    expect(enableConfirmation(onedrive)).toContain('刪除');
    expect(enableConfirmation(docs)).toBeNull();
  });

  it('puts OneDrive first and replaces updated cards', () => {
    const zeta = { ...docs, key: 'mcp:zeta', name: 'zeta' };
    expect(sortPlugins([zeta, docs, onedrive]).map((p) => p.key)).toEqual([
      'onedrive',
      'mcp:docs',
      'mcp:zeta',
    ]);
    expect(replacePlugin([onedrive, docs], { ...docs, enabled: false })[1].enabled).toBe(false);
  });
});
