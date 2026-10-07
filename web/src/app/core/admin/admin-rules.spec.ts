import {
  auditQueryParams,
  browserUtcOffsetMinutes,
  canStopRuntime,
  describeAuditAction,
  emptyAuditFilter,
  healthLevel,
  healthName,
  healthWarnings,
  localDayStartIso,
  runtimeStatusLabel,
  trendBars,
} from './admin-rules';

describe('admin rules', () => {
  it('reports the browser offset east-positive', () => {
    const date = new Date();
    expect(browserUtcOffsetMinutes(date)).toBe(-date.getTimezoneOffset());
  });

  it('labels runtime states and only lets live runtimes be stopped', () => {
    expect(runtimeStatusLabel('Running')).toBe('執行中');
    expect(canStopRuntime('Running')).toBe(true);
    expect(canStopRuntime('Busy')).toBe(true);
    expect(canStopRuntime('Stopped')).toBe(false);
    expect(canStopRuntime('Error')).toBe(false);
  });

  it('describes known actions and falls back to the code', () => {
    expect(describeAuditAction('admin.user.disable')).toBe('停用帳號');
    expect(describeAuditAction('custom.thing')).toBe('custom.thing');
  });

  it('turns local dates into ISO instants', () => {
    const iso = localDayStartIso('2026-10-06')!;
    const date = new Date(iso);
    expect([date.getFullYear(), date.getMonth(), date.getDate(), date.getHours()]).toEqual([
      2026, 9, 6, 0,
    ]);
    expect(localDayStartIso('10/06/2026')).toBeNull();
  });

  it('builds query params, making the end date inclusive', () => {
    expect(auditQueryParams(emptyAuditFilter())).toEqual({});
    const params = auditQueryParams(
      {
        action: 'admin.',
        result: 'Denied',
        from: '2026-10-01',
        to: '2026-10-06',
        userId: 'u1',
      },
      42,
    );
    expect(params['action']).toBe('admin.');
    expect(params['result']).toBe('Denied');
    expect(params['userId']).toBe('u1');
    expect(params['before']).toBe('42');
    expect(new Date(params['from']).getDate()).toBe(1);
    const to = new Date(params['to']);
    expect([to.getDate(), to.getHours()]).toEqual([7, 0]);
  });

  it('scales trend bars to the busiest day', () => {
    const bars = trendBars([
      { date: '2026-10-05', total: 2, failed: 0 },
      { date: '2026-10-06', total: '4', failed: 1 },
    ]);
    expect(bars.map((b) => [b.label, b.height, b.failed])).toEqual([
      ['10/5', 50, 0],
      ['10/6', 100, 1],
    ]);
    expect(trendBars([{ date: '2026-10-06', total: 0, failed: 0 }])[0].height).toBe(0);
  });
});

describe('service health rules', () => {
  it('名稱與狀態', () => {
    expect(healthName('database')).toBe('資料庫');
    expect(healthName('other')).toBe('other');
    expect(healthLevel('Healthy')).toBe('ok');
    expect(healthLevel('Degraded')).toBe('warn');
    expect(healthLevel('Unhealthy')).toBe('down');
  });

  it('異常的服務產生警告', () => {
    expect(
      healthWarnings([
        { name: 'database', status: 'Healthy' },
        { name: 'litellm', status: 'Unhealthy' },
      ]),
    ).toEqual(['LiteLLM（模型）目前無法使用，Agent 可能無法執行。']);
  });
});
