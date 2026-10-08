import { ConversationOneDrive, OneDriveStatus } from '../api/api-types';
import {
  onedriveFolderLabel,
  onedriveResultMessage,
  onedriveStateLabel,
  onedriveSyncSummary,
} from './onedrive-rules';

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

const sync = (overrides: Partial<ConversationOneDrive> = {}): ConversationOneDrive => ({
  availability: 'Ready',
  rootPath: '/Ymir',
  folderPath: 'chats/報告-1a2b3c4d',
  state: 'Synced',
  lastSyncedAt: '2026-10-07T09:30:00Z',
  conflictCount: 0,
  lastError: null,
  ...overrides,
});

const time = (iso: string) => `T(${iso})`;

describe('onedrive sync summary', () => {
  it('is hidden when the administrator has not enabled OneDrive', () => {
    expect(onedriveSyncSummary(sync({ availability: 'NotAllowed' }), time)).toBeNull();
    expect(onedriveSyncSummary(null, time)).toBeNull();
  });

  it('points to settings when the link is missing or expired', () => {
    for (const availability of ['NotConnected', 'NeedsReauth', 'NoRoot'] as const) {
      const summary = onedriveSyncSummary(sync({ availability }), time)!;
      expect(summary.needsSettings).toBe(true);
      expect(summary.canSync).toBe(false);
    }
  });

  it('shows the last sync time and conflict copies', () => {
    const summary = onedriveSyncSummary(sync({ conflictCount: 2 }), time)!;
    expect(summary.tone).toBe('ok');
    expect(summary.text).toContain('T(2026-10-07T09:30:00Z)');
    expect(summary.text).toContain('2 個衝突副本');
  });

  it('polls while pending and offers retry after a failure', () => {
    expect(onedriveSyncSummary(sync({ state: 'Pending' }), time)!.pending).toBe(true);
    const failed = onedriveSyncSummary(sync({ state: 'Failed', lastError: '暫時無法連線' }), time)!;
    expect(failed.tone).toBe('error');
    expect(failed.canSync).toBe(true);
    expect(failed.text).toContain('暫時無法連線');
  });

  it('explains that the first sync happens after a run', () => {
    const summary = onedriveSyncSummary(
      sync({ state: null, lastSyncedAt: null, folderPath: null }),
      time,
    )!;
    expect(summary.tone).toBe('hint');
    expect(summary.canSync).toBe(true);
  });

  it('builds the full folder label', () => {
    expect(onedriveFolderLabel(sync())).toBe('/Ymir/chats/報告-1a2b3c4d');
    expect(onedriveFolderLabel(sync({ folderPath: null }))).toBeNull();
  });
});
