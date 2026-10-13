import { ModelUsageKind, UserModelUsage, UserUsage } from '../api/api-types';
import {
  budgetLevel,
  formatRunTime,
  formatTokens,
  modelKindLabel,
  modelTotals,
  quotaLevel,
  spendShare,
  successRate,
  usageTotals,
  userModelRows,
} from './usage-rules';

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
  spendUsd: executions * 0.5,
  promptTokens: executions * 100,
  completionTokens: executions * 10,
  modelRequests: executions,
  budgetSpendUsd: null,
  budgetUsd: null,
  budgetResetAt: null,
  models: [],
});

const model = (
  modelId: string | null,
  executions: number,
  litellm: { requests: number | string; spend: number | string } | null,
  kind: ModelUsageKind = modelId === null ? 'Unattributed' : 'Catalog',
): UserModelUsage => ({
  modelId,
  displayName: modelId ?? '（未標示模型）',
  kind,
  executions,
  failed: 0,
  spendUsd: litellm?.spend ?? null,
  promptTokens: litellm ? Number(litellm.requests) * 10 : null,
  completionTokens: litellm ? Number(litellm.requests) * 5 : null,
  modelRequests: litellm?.requests ?? null,
});

const withModels = (displayName: string, ...models: UserModelUsage[]): UserUsage => ({
  ...usage(0, 0, 0, 0),
  displayName,
  models,
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
      spendUsd: 3,
      tokens: 660,
    });
  });
});

describe('model usage rules', () => {
  it('依本期預算標示', () => {
    expect(budgetLevel(5, null)).toBe('none');
    expect(budgetLevel(null, 10)).toBe('none');
    expect(budgetLevel(2, 10)).toBe('ok');
    expect(budgetLevel(8.5, 10)).toBe('near');
    expect(budgetLevel(10, 10)).toBe('reached');
  });

  it('token 數的顯示', () => {
    expect(formatTokens(null)).toBe('0');
    expect(formatTokens(950)).toBe('950');
    expect(formatTokens(1234)).toBe('1.2K');
    expect(formatTokens(12345)).toBe('12K');
    expect(formatTokens(2_500_000)).toBe('2.5M');
  });
});

describe('per-model usage rules', () => {
  it('同一個模型跨使用者合併，並計算使用人數', () => {
    const totals = modelTotals([
      withModels('a', { ...model('m1', 2, { requests: 3, spend: 0.3 }), failed: 1 }),
      withModels('b', model('m1', 1, { requests: 1, spend: 0.1 }), model('m2', 4, null)),
    ]);
    expect(totals.map((t) => t.modelId)).toEqual(['m1', 'm2']);
    expect(totals[0]).toEqual({
      modelId: 'm1',
      displayName: 'm1',
      kind: 'Catalog',
      users: 2,
      executions: 3,
      failed: 1,
      modelRequests: 4,
      promptTokens: 40,
      completionTokens: 20,
      spendUsd: expect.closeTo(0.4, 10),
    });
    expect(totals[1].users).toBe(1);
  });

  it('LiteLLM 欄位全部沒有資料時維持 null，只加總有值的部分', () => {
    const [none] = modelTotals([
      withModels('a', model('m1', 1, null)),
      withModels('b', model('m1', 2, null)),
    ]);
    expect(none.modelRequests).toBeNull();
    expect(none.promptTokens).toBeNull();
    expect(none.completionTokens).toBeNull();
    expect(none.spendUsd).toBeNull();
    expect(none.executions).toBe(3);

    // 一位使用者的 LiteLLM 查詢失敗（null）不會讓其他人的資料變成 0 或 null
    const [some] = modelTotals([
      withModels('a', model('m1', 1, null)),
      withModels('b', model('m1', 1, { requests: 2, spend: 0.5 })),
    ]);
    expect(some.modelRequests).toBe(2);
    expect(some.promptTokens).toBe(20);
    expect(some.spendUsd).toBe(0.5);
  });

  it('依費用、模型請求、執行次數排序，未標示模型最後', () => {
    const totals = modelTotals([
      withModels(
        'a',
        model(null, 9, { requests: 9, spend: 9 }),
        model('few-requests', 9, { requests: 1, spend: 0.2 }),
        model('many-requests', 1, { requests: 5, spend: 0.2 }),
        model('expensive', 1, { requests: 1, spend: 1 }),
        model('no-litellm', 3, null),
        model('zero-b', 2, { requests: 0, spend: 0 }),
        model('zero-a', 2, { requests: 0, spend: 0 }),
        model('zero-more-runs', 5, { requests: 0, spend: 0 }),
      ),
    ]);
    expect(totals.map((t) => t.modelId)).toEqual([
      'expensive',
      'many-requests',
      'few-requests',
      'zero-more-runs',
      'zero-a',
      'zero-b',
      'no-litellm',
      null,
    ]);
  });

  it('產生的型別可能是字串數字', () => {
    const [total] = modelTotals([
      withModels('a', {
        ...model('m1', 0, { requests: '2', spend: '0.25' }),
        executions: '3',
        failed: '1',
        promptTokens: '100',
        completionTokens: '40',
      }),
      withModels('b', model('m1', 1, { requests: '1', spend: '0.5' })),
    ]);
    expect(total).toMatchObject({
      executions: 4,
      failed: 1,
      modelRequests: 3,
      promptTokens: 110,
      completionTokens: 45,
      spendUsd: 0.75,
    });
  });

  it('每人每模型的表格列：第一列帶 rowspan，沒有模型用量的使用者不出現', () => {
    const a = withModels('a', model('m1', 1, null), model('m2', 1, null), model(null, 1, null));
    const empty = withModels('empty');
    const b = withModels('b', model('m2', 1, null));
    const rows = userModelRows([a, empty, b]);
    expect(rows.map((r) => [r.displayName, r.model.modelId, r.span])).toEqual([
      ['a', 'm1', 3],
      ['a', 'm2', 0],
      ['a', null, 0],
      ['b', 'm2', 1],
    ]);
    expect(rows[0].userId).toBe(a.userId);
    expect(rows[3].userId).toBe(b.userId);
  });

  it('費用佔比', () => {
    expect(spendShare(0.25, 1)).toBe(25);
    expect(spendShare('0.5', 3)).toBe(17);
    expect(spendShare(0, 0)).toBeNull();
    expect(spendShare(null, 1)).toBeNull();
  });

  it('模型種類的標籤', () => {
    expect(modelKindLabel('Catalog')).toBeNull();
    expect(modelKindLabel('Embedding')).toBe('知識庫向量');
    expect(modelKindLabel('Unlisted')).toBe('不在模型清單');
    expect(modelKindLabel('Unattributed')).toBeNull();
  });
});
