import { UserUsage } from '../api/api-types';
import { formatRunTime, quotaLevel, successRate, usageTotals } from './usage-rules';

const usage = (
  executions: number,
  completed: number,
  failed: number,
  runMinutes: number,
): UserUsage => ({
  userId: crypto.randomUUID(),
  displayName: 'user',
  executions,
  completed,
  failed,
  cancelled: executions - completed - failed,
  runMinutes,
  last24Hours: executions,
  lastExecutionAt: null,
  runtimeStatus: null,
});

describe('usage rules', () => {
  it('依每日上限標示', () => {
    expect(quotaLevel(10, 0)).toBe('none');
    expect(quotaLevel(3, 10)).toBe('ok');
    expect(quotaLevel(8, 10)).toBe('near');
    expect(quotaLevel(10, 10)).toBe('reached');
  });

  it('執行時間的顯示', () => {
    expect(formatRunTime(0)).toBe('—');
    expect(formatRunTime(0.4)).toBe('不到 1 分鐘');
    expect(formatRunTime(12.6)).toBe('13 分鐘');
    expect(formatRunTime(60)).toBe('1 小時');
    expect(formatRunTime(75)).toBe('1 小時 15 分');
  });

  it('成功率與合計', () => {
    expect(successRate(usage(4, 3, 1, 2))).toBe(75);
    expect(successRate(usage(0, 0, 0, 0))).toBeNull();
    expect(usageTotals([usage(4, 3, 1, 2), usage(2, 2, 0, 1.5)])).toEqual({
      users: 2,
      executions: 6,
      failed: 1,
      runMinutes: 3.5,
    });
  });
});
