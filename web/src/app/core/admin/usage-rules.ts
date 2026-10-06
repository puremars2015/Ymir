import { UserUsage } from '../api/api-types';

export type QuotaLevel = 'none' | 'ok' | 'near' | 'reached';

/** 與每日上限比較：80% 以上提醒，達到上限標紅；沒有上限時不標示。 */
export function quotaLevel(last24Hours: number | string, dailyLimit: number | string): QuotaLevel {
  const limit = Number(dailyLimit);
  if (!limit) return 'none';
  const used = Number(last24Hours);
  if (used >= limit) return 'reached';
  return used >= limit * 0.8 ? 'near' : 'ok';
}

/** 0.4 分鐘 → 「不到 1 分鐘」；75 → 「1 小時 15 分」。 */
export function formatRunTime(minutes: number | string): string {
  const value = Number(minutes);
  if (value <= 0) return '—';
  if (value < 1) return '不到 1 分鐘';
  const rounded = Math.round(value);
  const hours = Math.floor(rounded / 60);
  const rest = rounded % 60;
  if (!hours) return `${rest} 分鐘`;
  return rest ? `${hours} 小時 ${rest} 分` : `${hours} 小時`;
}

/** 成功率（完成 / 全部）；沒有執行時為 null。 */
export function successRate(usage: UserUsage): number | null {
  const total = Number(usage.executions);
  return total ? Math.round((Number(usage.completed) / total) * 100) : null;
}

export interface UsageTotals {
  users: number;
  executions: number;
  failed: number;
  runMinutes: number;
}

export function usageTotals(users: UserUsage[]): UsageTotals {
  return users.reduce(
    (sum, u) => ({
      users: sum.users + 1,
      executions: sum.executions + Number(u.executions),
      failed: sum.failed + Number(u.failed),
      runMinutes: sum.runMinutes + Number(u.runMinutes),
    }),
    { users: 0, executions: 0, failed: 0, runMinutes: 0 },
  );
}
