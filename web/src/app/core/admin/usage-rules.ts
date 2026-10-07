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
  spendUsd: number;
  tokens: number;
}

/** 本期預算的使用程度：80% 以上提醒，用完標紅；沒有預算或沒有資料時不標示。 */
export function budgetLevel(
  spend: number | string | null | undefined,
  budget: number | string | null | undefined,
): QuotaLevel {
  const limit = Number(budget ?? 0);
  if (!limit || spend === null || spend === undefined) return 'none';
  const used = Number(spend);
  if (used >= limit) return 'reached';
  return used >= limit * 0.8 ? 'near' : 'ok';
}

/** 950 → 「950」；12,345 → 「12.3K」；2,500,000 → 「2.5M」。 */
export function formatTokens(value: number | string | null | undefined): string {
  const tokens = Number(value ?? 0);
  if (tokens < 1000) return String(tokens);
  if (tokens < 1_000_000) return `${(tokens / 1000).toFixed(tokens < 10_000 ? 1 : 0)}K`;
  return `${(tokens / 1_000_000).toFixed(1)}M`;
}

export function usageTotals(users: UserUsage[]): UsageTotals {
  return users.reduce(
    (sum, u) => ({
      users: sum.users + 1,
      executions: sum.executions + Number(u.executions),
      failed: sum.failed + Number(u.failed),
      runMinutes: sum.runMinutes + Number(u.runMinutes),
      spendUsd: sum.spendUsd + Number(u.spendUsd ?? 0),
      tokens: sum.tokens + Number(u.promptTokens ?? 0) + Number(u.completionTokens ?? 0),
    }),
    { users: 0, executions: 0, failed: 0, runMinutes: 0, spendUsd: 0, tokens: 0 },
  );
}
