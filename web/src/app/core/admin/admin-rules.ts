import { AuditResult, DailyExecution, RuntimeState } from '../api/api-types';

/** 瀏覽器目前的 UTC 位移（分鐘，東區為正），讓伺服器以使用者的「今天」切日。 */
export const browserUtcOffsetMinutes = (date = new Date()): number => -date.getTimezoneOffset();

const RUNTIME_LABELS: Record<RuntimeState, string> = {
  NotCreated: '尚未建立',
  Created: '已建立',
  Running: '執行中',
  Busy: '工作中',
  Stopped: '已停止',
  Error: '錯誤',
  Deleted: '已刪除',
};

export const runtimeStatusLabel = (status: RuntimeState): string =>
  RUNTIME_LABELS[status] ?? status;

/** 只有在跑的 runtime 才需要「停止」。 */
export const canStopRuntime = (status: RuntimeState): boolean =>
  status === 'Running' || status === 'Busy' || status === 'Created';

const ACTION_LABELS: Record<string, string> = {
  'runtime.idle_stop': '閒置自動停止執行環境',
  'runtime.create': '建立執行環境',
  'runtime.start': '啟動執行環境',
  'runtime.stop': '停止執行環境',
  'runtime.reconcile': '同步執行環境狀態',
  'runtime.ensure': '執行環境啟動失敗',
  'admin.settings.runtime.update': '修改執行政策',
  'admin.settings.runtime.reset': '還原執行政策',
  'auth.login': '開發登入',
  'auth.login.oidc': '企業帳號登入',
  'auth.login.password': '帳號密碼登入',
  'auth.logout': '登出',
  'auth.password.change': '變更密碼',
  'admin.user.create': '建立本機帳號',
  'admin.user.disable': '停用帳號',
  'admin.user.enable': '啟用帳號',
  'admin.user.reset_password': '重設密碼',
  'admin.runtime.stop': '停止執行環境',
  'admin.make_topic.create': '新增 Make 主題',
  'admin.make_topic.update': '修改 Make 主題',
  'admin.make_topic.delete': '刪除 Make 主題',
  'execution.create': '送出訊息',
  'execution.start': '開始執行',
  'execution.finish': '執行結束',
  'execution.cancel': '取消執行',
  'project.create': '建立專案',
  'project.update': '修改專案',
};

/** 動作的中文說明；未知的動作直接顯示代碼。 */
export const describeAuditAction = (action: string): string => ACTION_LABELS[action] ?? action;

/** 篩選選單：動作前綴（空字串為全部）。 */
export const AUDIT_ACTION_FILTERS: readonly { value: string; label: string }[] = [
  { value: '', label: '全部動作' },
  { value: 'auth.', label: '登入 / 登出' },
  { value: 'admin.', label: '管理操作' },
  { value: 'execution.', label: 'Agent 執行' },
  { value: 'project.', label: '專案' },
];

export const AUDIT_RESULT_LABELS: Record<AuditResult, string> = {
  Success: '成功',
  Failure: '失敗',
  Denied: '拒絕',
};

export interface AuditFilterForm {
  action: string;
  result: AuditResult | '';
  /** `yyyy-MM-dd`（input type=date），以瀏覽器時區解讀 */
  from: string;
  to: string;
  userId: string | null;
}

export const emptyAuditFilter = (): AuditFilterForm => ({
  action: '',
  result: '',
  from: '',
  to: '',
  userId: null,
});

/** `yyyy-MM-dd` → 當地當天 00:00 的 ISO 字串（含位移）。 */
export function localDayStartIso(day: string): string | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(day);
  if (!match) {
    return null;
  }
  const date = new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

function addDays(day: string, days: number): string | null {
  const start = localDayStartIso(day);
  if (!start) {
    return null;
  }
  const date = new Date(start);
  date.setDate(date.getDate() + days);
  return date.toISOString();
}

/** 篩選表單 → 查詢參數；結束日期包含當天（送出隔天 00:00，伺服器為不含）。 */
export function auditQueryParams(
  filter: AuditFilterForm,
  before: number | null = null,
): Record<string, string> {
  const params: Record<string, string> = {};
  if (filter.action) params['action'] = filter.action;
  if (filter.result) params['result'] = filter.result;
  if (filter.userId) params['userId'] = filter.userId;
  const from = filter.from ? localDayStartIso(filter.from) : null;
  if (from) params['from'] = from;
  const to = filter.to ? addDays(filter.to, 1) : null;
  if (to) params['to'] = to;
  if (before !== null) params['before'] = String(before);
  return params;
}

export interface TrendBar {
  date: string;
  label: string;
  total: number;
  failed: number;
  /** 長條高度（0～100，最大值為 100；全部為 0 時都是 0） */
  height: number;
}

/** 近幾天執行數 → 長條圖資料。 */
export function trendBars(trend: DailyExecution[]): TrendBar[] {
  const totals = trend.map((d) => Number(d.total));
  const max = Math.max(0, ...totals);
  return trend.map((d, i) => {
    const [, month, day] = d.date.split('-');
    return {
      date: d.date,
      label: `${Number(month)}/${Number(day)}`,
      total: totals[i],
      failed: Number(d.failed),
      height: max === 0 ? 0 : Math.round((totals[i] / max) * 100),
    };
  });
}

const HEALTH_NAMES: Record<string, string> = {
  database: '資料庫',
  runtime: '執行環境',
  litellm: 'LiteLLM（模型）',
};

/** 健康檢查項目的顯示名稱。 */
export const healthName = (name: string): string => HEALTH_NAMES[name] ?? name;

export type HealthLevel = 'ok' | 'warn' | 'down';

export function healthLevel(status: string): HealthLevel {
  if (status === 'Healthy') return 'ok';
  return status === 'Degraded' ? 'warn' : 'down';
}

/** 有服務異常時，總覽最上方顯示的警告。 */
export function healthWarnings(checks: { name: string; status: string }[]): string[] {
  return checks
    .filter((c) => healthLevel(c.status) === 'down')
    .map((c) => `${healthName(c.name)}目前無法使用，Agent 可能無法執行。`);
}
