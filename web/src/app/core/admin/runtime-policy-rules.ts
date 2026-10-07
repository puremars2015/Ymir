import { RuntimePolicy, RuntimePolicyValues, SaveRuntimePolicyRequest } from '../api/api-types';

/** 執行政策表單（ADR-0011）。數字欄位以字串保存，讓使用者可以暫時清空再輸入。 */
export interface RuntimePolicyDraft {
  idleTimeoutMinutes: string;
  executionTimeoutMinutes: string;
  maxPendingExecutionsPerUser: string;
  dailyExecutionLimit: string;
  monthlyBudgetUsd: string;
}

/** 與後端 RuntimePolicySettings.Validate 相同的範圍。 */
export const POLICY_LIMITS = {
  idleTimeoutMinutes: { min: 0, max: 1440 },
  executionTimeoutMinutes: { min: 1, max: 240 },
  maxPendingExecutionsPerUser: { min: 1, max: 50 },
  dailyExecutionLimit: { min: 0, max: 10000 },
  monthlyBudgetUsd: { min: 0, max: 100000 },
} as const;

const LABELS: Record<keyof RuntimePolicyDraft, string> = {
  idleTimeoutMinutes: '閒置停止時間',
  executionTimeoutMinutes: '單次執行上限',
  maxPendingExecutionsPerUser: '每人同時排隊的工作數',
  dailyExecutionLimit: '每人每日執行次數',
  monthlyBudgetUsd: '每人每月模型預算',
};

/** 部署設定可能是小數（例如測試用的 0.005 分鐘）；表單只接受整數分鐘，四捨五入且不低於下限。 */
export function policyDraftFrom(values: RuntimePolicyValues): RuntimePolicyDraft {
  const round = (value: number | string, min: number) =>
    String(Math.max(min, Math.round(Number(value))));
  return {
    idleTimeoutMinutes: round(values.idleTimeoutMinutes, 0),
    executionTimeoutMinutes: round(values.executionTimeoutMinutes, 1),
    maxPendingExecutionsPerUser: round(values.maxPendingExecutionsPerUser, 1),
    dailyExecutionLimit: round(values.dailyExecutionLimit, 0),
    monthlyBudgetUsd: String(Number(values.monthlyBudgetUsd)),
  };
}

export function policyDraftProblem(draft: RuntimePolicyDraft): string | null {
  const budget = draft.monthlyBudgetUsd.trim();
  const { max: maxBudget } = POLICY_LIMITS.monthlyBudgetUsd;
  if (!/^\d+(\.\d{1,2})?$/.test(budget) || Number(budget) > maxBudget) {
    return `每人每月模型預算必須是 0～${maxBudget} 美元，最多兩位小數。`;
  }
  const integers = Object.keys(POLICY_LIMITS).filter(
    (key) => key !== 'monthlyBudgetUsd',
  ) as (keyof RuntimePolicyDraft)[];
  for (const key of integers) {
    const text = draft[key].trim();
    const value = Number(text);
    const { min, max } = POLICY_LIMITS[key as keyof typeof POLICY_LIMITS];
    if (!/^\d+$/.test(text) || value < min || value > max) {
      return `${LABELS[key]}必須是 ${min}～${max} 的整數。`;
    }
  }
  return null;
}

export const toSavePolicyRequest = (draft: RuntimePolicyDraft): SaveRuntimePolicyRequest => ({
  idleTimeoutMinutes: Number(draft.idleTimeoutMinutes),
  executionTimeoutMinutes: Number(draft.executionTimeoutMinutes),
  maxPendingExecutionsPerUser: Number(draft.maxPendingExecutionsPerUser),
  dailyExecutionLimit: Number(draft.dailyExecutionLimit),
  monthlyBudgetUsd: Number(draft.monthlyBudgetUsd),
});

/** 30 分鐘、1.5 小時、2 小時；0 → 「不自動停止」。 */
export function formatMinutes(value: number | string, zeroLabel = '不限制'): string {
  const minutes = Number(value);
  if (!minutes) return zeroLabel;
  if (minutes < 1) return `${Math.round(minutes * 60)} 秒`;
  if (minutes < 60 || minutes % 30 !== 0) return `${Math.round(minutes)} 分鐘`;
  return `${minutes / 60} 小時`;
}

/** US$12.50；0 → 「不限制」（預算）。 */
export const formatBudget = (value: number | string): string =>
  Number(value) ? formatUsd(value) : '不限制';

export function formatUsd(value: number | string): string {
  const amount = Number(value);
  if (amount > 0 && amount < 0.01) return '< US$0.01';
  return `US$${amount.toFixed(2)}`;
}

export const formatLimit = (value: number | string, unit: string): string =>
  Number(value) ? `${value} ${unit}` : '不限制';

/** 摘要一行，顯示在卡片標題下方。 */
export function policySummary(policy: RuntimePolicy): string {
  const e = policy.effective;
  return [
    `閒置 ${formatMinutes(e.idleTimeoutMinutes, '不自動停止')}後停止`,
    `單次最長 ${formatMinutes(e.executionTimeoutMinutes)}`,
    `每人排隊 ${e.maxPendingExecutionsPerUser} 個`,
    `每日 ${formatLimit(e.dailyExecutionLimit, '次')}`,
    `每月預算 ${formatBudget(e.monthlyBudgetUsd)}`,
  ].join(' · ');
}
