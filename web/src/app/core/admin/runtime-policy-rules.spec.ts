import {
  formatBudget,
  formatLimit,
  formatUsd,
  formatMinutes,
  policyDraftFrom,
  policyDraftProblem,
  policySummary,
  toSavePolicyRequest,
} from './runtime-policy-rules';

describe('runtime policy rules', () => {
  const values = {
    idleTimeoutMinutes: 30,
    executionTimeoutMinutes: 30,
    maxPendingExecutionsPerUser: 5,
    dailyExecutionLimit: 0,
    monthlyBudgetUsd: 0,
  };

  it('表單由目前的值產生，小數四捨五入且不低於下限', () => {
    expect(policyDraftFrom(values)).toEqual({
      idleTimeoutMinutes: '30',
      executionTimeoutMinutes: '30',
      maxPendingExecutionsPerUser: '5',
      dailyExecutionLimit: '0',
      monthlyBudgetUsd: '0',
    });
    expect(
      policyDraftFrom({ ...values, executionTimeoutMinutes: 0.005 }).executionTimeoutMinutes,
    ).toBe('1');
  });

  it('檢查範圍與整數', () => {
    const draft = policyDraftFrom(values);
    expect(policyDraftProblem(draft)).toBeNull();
    expect(policyDraftProblem({ ...draft, idleTimeoutMinutes: '1441' })).toContain('閒置停止時間');
    expect(policyDraftProblem({ ...draft, executionTimeoutMinutes: '0' })).toContain(
      '單次執行上限',
    );
    expect(policyDraftProblem({ ...draft, maxPendingExecutionsPerUser: '2.5' })).toContain('排隊');
    expect(policyDraftProblem({ ...draft, dailyExecutionLimit: '' })).toContain('每日');
    expect(policyDraftProblem({ ...draft, monthlyBudgetUsd: '12.50' })).toBeNull();
    expect(policyDraftProblem({ ...draft, monthlyBudgetUsd: '1.234' })).toContain('預算');
    expect(policyDraftProblem({ ...draft, monthlyBudgetUsd: '-1' })).toContain('預算');
  });

  it('轉成 API 請求', () => {
    expect(toSavePolicyRequest(policyDraftFrom(values))).toEqual(values);
  });

  it('時間與上限的顯示', () => {
    expect(formatMinutes(0, '不自動停止')).toBe('不自動停止');
    expect(formatMinutes(0.005)).toBe('0 秒');
    expect(formatMinutes(0.5)).toBe('30 秒');
    expect(formatMinutes(45)).toBe('45 分鐘');
    expect(formatMinutes(90)).toBe('1.5 小時');
    expect(formatMinutes(120)).toBe('2 小時');
    expect(formatLimit(0, '次')).toBe('不限制');
    expect(formatLimit(100, '次')).toBe('100 次');
    expect(
      policySummary({
        effective: values,
        deployment: values,
        source: 'Deployment',
        updatedAt: null,
        updatedByName: null,
      }),
    ).toBe('閒置 30 分鐘後停止 · 單次最長 30 分鐘 · 每人排隊 5 個 · 每日 不限制 · 每月預算 不限制');
    expect(formatUsd(12.5)).toBe('US$12.50');
    expect(formatUsd(0.004)).toBe('< US$0.01');
    expect(formatBudget(0)).toBe('不限制');
  });
});
