import { OneDriveStatus } from '../api/api-types';
import { onedriveResultMessage, onedriveStateLabel } from './onedrive-rules';

const status = (overrides: Partial<OneDriveStatus> = {}): OneDriveStatus => ({
  allowed: true,
  available: true,
  state: 'NotConnected',
  account: null,
  connectedAt: null,
  lastError: null,
  ...overrides,
});

describe('onedrive rules', () => {
  it('maps callback results to messages', () => {
    expect(onedriveResultMessage('connected')?.error).toBe(false);
    expect(onedriveResultMessage('mismatch')?.text).toContain('相同的公司帳號');
    expect(onedriveResultMessage('error')?.error).toBe(true);
    expect(onedriveResultMessage(null)).toBeNull();
    expect(onedriveResultMessage('<script>')).toBeNull();
  });

  it('describes the link state', () => {
    expect(onedriveStateLabel(status())).toBe('尚未連結');
    // OneDrive 只是 Agent 外掛（ADR-0033）：沒有同步資料夾。
    expect(onedriveStateLabel(status({ state: 'Connected' }))).toBe('已連結');
    expect(onedriveStateLabel(status({ state: 'NeedsReauth' }))).toContain('重新連結');
  });
});
