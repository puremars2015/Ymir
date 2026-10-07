import { OneDriveStatus } from '../api/api-types';
import { onedriveResultMessage, onedriveStateLabel } from './onedrive-rules';

const status = (overrides: Partial<OneDriveStatus> = {}): OneDriveStatus => ({
  allowed: true,
  available: true,
  state: 'NotConnected',
  account: null,
  rootPath: null,
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
    expect(onedriveStateLabel(status({ state: 'Connected' }))).toContain('尚未設定');
    expect(onedriveStateLabel(status({ state: 'Connected', rootPath: '/Ymir' }))).toBe(
      '已連結，同步到 /Ymir',
    );
    expect(onedriveStateLabel(status({ state: 'NeedsReauth' }))).toContain('重新連結');
  });
});
