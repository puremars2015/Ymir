import { ModelUsageKind, UserModelUsage, UserUsage } from '../api/api-types';

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

/**
 * 模型種類的標籤（ADR-0021）：清單內的模型與「未標示模型」不加標籤，
 * 只有知識庫的 embedding 與已不在模型清單的模型需要提醒管理員。
 */
export function modelKindLabel(kind: ModelUsageKind): string | null {
  switch (kind) {
    case 'Embedding':
      return '知識庫向量';
    case 'Unlisted':
      return '不在模型清單';
    default:
      return null;
  }
}

/** 所有使用者合計的單一模型用量；LiteLLM 欄位為 null 表示沒有任何使用者有 LiteLLM 資料。 */
export interface ModelUsageTotal {
  modelId: string | null;
  displayName: string;
  /** 管理介面顯示「名稱 (provider)」（ADR-0020 補充）。 */
  provider?: string | null;
  kind: ModelUsageKind;
  users: number;
  executions: number;
  failed: number;
  modelRequests: number | null;
  promptTokens: number | null;
  completionTokens: number | null;
  spendUsd: number | null;
}

/** null 表示「沒有資料」而不是 0（LiteLLM 查詢失敗的使用者），所以只加總有值的部分。 */
function addKnown(sum: number | null, value: number | string | null): number | null {
  return value === null ? sum : (sum ?? 0) + Number(value);
}

/**
 * 依模型合計所有使用者（ADR-0021）：同一個 modelId（含 null 的「未標示模型」）合併成一列，
 * 「使用人數」是有這個模型用量的使用者數。排序：費用、模型請求、執行次數由多到少，未標示模型最後。
 */
export function modelTotals(users: UserUsage[]): ModelUsageTotal[] {
  const byModel = new Map<string | null, ModelUsageTotal>();
  for (const user of users) {
    for (const m of user.models) {
      const total = byModel.get(m.modelId) ?? {
        modelId: m.modelId,
        displayName: m.displayName,
        provider: m.provider,
        kind: m.kind,
        users: 0,
        executions: 0,
        failed: 0,
        modelRequests: null,
        promptTokens: null,
        completionTokens: null,
        spendUsd: null,
      };
      byModel.set(m.modelId, {
        ...total,
        users: total.users + 1,
        executions: total.executions + Number(m.executions),
        failed: total.failed + Number(m.failed),
        modelRequests: addKnown(total.modelRequests, m.modelRequests),
        promptTokens: addKnown(total.promptTokens, m.promptTokens),
        completionTokens: addKnown(total.completionTokens, m.completionTokens),
        spendUsd: addKnown(total.spendUsd, m.spendUsd),
      });
    }
  }
  return [...byModel.values()].sort(
    (a, b) =>
      Number(a.kind === 'Unattributed') - Number(b.kind === 'Unattributed') ||
      (b.spendUsd ?? -1) - (a.spendUsd ?? -1) ||
      (b.modelRequests ?? -1) - (a.modelRequests ?? -1) ||
      b.executions - a.executions ||
      a.displayName.localeCompare(b.displayName),
  );
}

/** 每人每模型表格的一列；span 是使用者欄的 rowspan，0 表示這列不畫使用者欄。 */
export interface UserModelRow {
  userId: string;
  displayName: string;
  span: number;
  model: UserModelUsage;
}

/** 攤平成表格列：保留 API 的使用者順序與伺服器端的模型排序，沒有模型用量的使用者不出現。 */
export function userModelRows(users: UserUsage[]): UserModelRow[] {
  return users.flatMap((user) =>
    user.models.map((model, index) => ({
      userId: user.userId,
      displayName: user.displayName,
      span: index === 0 ? user.models.length : 0,
      model,
    })),
  );
}

/** 佔總費用的百分比（四捨五入）；沒有費用資料或總費用為 0 時為 null。 */
export function spendShare(part: number | string | null, total: number): number | null {
  if (part === null || total <= 0) return null;
  return Math.round((Number(part) / total) * 100);
}
