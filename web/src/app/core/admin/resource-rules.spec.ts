import { ContainerResource, ReclamationPolicy, ResourceSnapshot } from '../api/api-types';
import {
  eligibilityLabel,
  formatBytes,
  formatPercent,
  freedBytes,
  pressureAlerts,
  pressureKeyLabel,
  reclamationDraftFrom,
  reclamationDraftProblem,
  reclamationSummary,
  reclaimStateLabel,
  toReclamationPolicy,
} from './resource-rules';

const off: ReclamationPolicy = {
  enabled: false,
  containerRetentionDays: 0,
  imageRetentionDays: 0,
  cpuMinFreePercent: 0,
  memoryMinAvailableGb: 0,
  memoryMinAvailablePercent: 0,
  diskMinAvailableGb: 0,
  diskMinAvailablePercent: 0,
};

const container = (patch: Partial<ContainerResource>): ContainerResource => ({
  store: 'ymir',
  name: 'ymir-user-x',
  shortId: 'abc',
  image: 'localhost/ymir/agent-runtime:1',
  isAgentRuntime: true,
  state: 'exited',
  health: null,
  cpuPercent: null,
  memoryBytes: null,
  unusedSince: null,
  protection: null,
  retentionEligible: false,
  ...patch,
});

describe('resource-rules', () => {
  it('formats sizes in decimal units and never shows unknown as zero', () => {
    expect(formatBytes(0)).toBe('0 B');
    expect(formatBytes(363_331_660)).toBe('363 MB');
    expect(formatBytes('16230000000')).toBe('16.2 GB');
    expect(formatBytes(null)).toBe('—');
    expect(formatPercent(undefined)).toBe('—');
    expect(formatPercent(40)).toBe('40.0%');
  });

  it('labels eligibility from protection and retention', () => {
    expect(eligibilityLabel(container({ protection: 'Active' }))).toBe('執行中／啟動中');
    expect(eligibilityLabel(container({ retentionEligible: true }))).toContain('已超過保留期限');
    expect(eligibilityLabel(container({}))).toContain('資源不足時');
  });

  it('explains reclaim state, pressure keys and measured freed space', () => {
    expect(reclaimStateLabel('LocksUnavailable')).toContain('只提供監控與 dry run');
    expect(pressureKeyLabel('disk:fs-1234')).toBe('磁碟 fs-1234');
    expect(
      freedBytes([
        { availableBeforeBytes: 100, availableAfterBytes: 300 },
        { availableBeforeBytes: 500, availableAfterBytes: 400 },
      ]),
    ).toBe(200);
  });

  it('validates the draft with the same ranges as the server', () => {
    const draft = reclamationDraftFrom(off);
    expect(reclamationDraftProblem(draft)).toBeNull();
    expect(reclamationDraftProblem({ ...draft, containerRetentionDays: '1.5' })).toContain(
      '保留天數',
    );
    expect(reclamationDraftProblem({ ...draft, imageRetentionDays: '' })).toContain('保留天數');
    expect(reclamationDraftProblem({ ...draft, diskMinAvailablePercent: '100' })).toContain('比例');
    expect(reclamationDraftProblem({ ...draft, memoryMinAvailableGb: '-1' })).toContain('容量');
    expect(toReclamationPolicy({ ...draft, enabled: true, imageRetentionDays: '30' })).toEqual({
      ...off,
      enabled: true,
      imageRetentionDays: 30,
    });
  });

  it('summarises that zero days means no age-based reclaim', () => {
    expect(reclamationSummary(off)).toContain('不會刪除');
    const summary = reclamationSummary({
      ...off,
      enabled: true,
      imageRetentionDays: 30,
      diskMinAvailablePercent: 10,
    });
    expect(summary).toContain('執行環境不依期限回收');
    expect(summary).toContain('image 30 天後回收');
    expect(summary).toContain('磁碟 < 10%');
  });

  describe('pressureAlerts', () => {
    const snapshot = (patch: Partial<ResourceSnapshot>): ResourceSnapshot => ({
      generatedAt: '2026-10-15T00:00:00Z',
      host: {
        status: 'Available',
        sampledAt: '2026-10-15T00:00:00Z',
        cpuCount: 4,
        cpuUsedPercent: 95,
        cpuFreePercent: 5,
        memoryTotalBytes: 16e9,
        memoryAvailableBytes: 8e9,
        memoryAvailablePercent: 50,
      },
      filesystems: [
        { id: 'fs-a', stores: ['root'], totalBytes: 100, availableBytes: 5, availablePercent: 5 },
        { id: 'fs-b', stores: ['ymir'], totalBytes: 100, availableBytes: 50, availablePercent: 50 },
      ],
      stores: [],
      containers: [],
      images: [],
      pressure: [],
      reclaimState: 'Active',
      timing: {
        sampleIntervalSeconds: 30,
        sustainMinutes: 5,
        cooldownMinutes: 10,
        recoveryMarginPercent: 5,
        maxDeletionsPerRound: 10,
        keepRecentVersions: 3,
        retentionSweepMinutes: 60,
      },
      ...patch,
    });
    const below = (key: string, triggered: boolean) => ({
      key,
      below: true,
      belowSince: '2026-10-14T23:50:00Z',
      triggered,
    });

    it('does not alert when nothing is below or the sustain period has not elapsed', () => {
      expect(pressureAlerts(snapshot({}))).toEqual([]);
      expect(
        pressureAlerts(
          snapshot({
            pressure: [{ key: 'cpu', below: false, belowSince: null, triggered: false }],
          }),
        ),
      ).toEqual([]);
      expect(pressureAlerts(snapshot({ pressure: [below('cpu', false)] }))).toEqual([]);
    });

    it('warns that nothing happens automatically when reclaim is not active', () => {
      const [alert] = pressureAlerts(
        snapshot({ reclaimState: 'LocksUnavailable', pressure: [below('memory', false)] }),
      );
      expect(alert.message).toContain('記憶體低於門檻');
      expect(alert.message).toContain('不會自動處理');
    });

    it('says CPU pressure persists without safe candidates and running work is not stopped', () => {
      const [alert] = pressureAlerts(
        snapshot({
          pressure: [below('cpu', true)],
          containers: [container({ protection: 'Active' })],
        }),
      );
      expect(alert.message).toContain('已沒有可安全回收的項目');
      expect(alert.message).toContain('不會停止執行中的工作');
    });

    it('counts remaining disk candidates only on the pressured filesystem', () => {
      const alerts = pressureAlerts(
        snapshot({
          pressure: [below('disk:fs-a', true)],
          containers: [container({ store: 'ymir' }), container({ store: 'root' })],
        }),
      );
      expect(alerts).toHaveLength(1);
      expect(alerts[0].message).toContain('還有 1 個可回收項目');

      const none = pressureAlerts(
        snapshot({
          pressure: [below('disk:fs-a', true)],
          containers: [container({ store: 'ymir' })],
        }),
      );
      expect(none[0].message).toContain('已沒有可安全回收的項目');
    });
  });
});
