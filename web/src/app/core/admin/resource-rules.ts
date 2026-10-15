import {
  ContainerResource,
  DataStatus,
  ImageResource,
  ProtectionReason,
  ReclaimState,
  ReclaimTrigger,
  ReclamationPolicy,
  ResourceSnapshot,
} from '../api/api-types';

/** 系統資源（ADR-0025）的顯示規則：全部是純函式。 */

const num = (value: number | string | null | undefined): number | null => {
  if (value === null || value === undefined || value === '') return null;
  const n = typeof value === 'number' ? value : Number(value);
  return Number.isFinite(n) ? n : null;
};

/** 十進位單位（與 podman、df 相同）；null 顯示「—」而不是 0（查詢失敗不可當作零）。 */
export function formatBytes(value: number | string | null | undefined): string {
  const bytes = num(value);
  if (bytes === null) return '—';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let size = bytes;
  let unit = 0;
  while (size >= 1000 && unit < units.length - 1) {
    size /= 1000;
    unit++;
  }
  return `${unit === 0 ? size : size.toFixed(size >= 100 ? 0 : 1)} ${units[unit]}`;
}

export function formatPercent(value: number | string | null | undefined): string {
  const n = num(value);
  return n === null ? '—' : `${n.toFixed(1)}%`;
}

const PROTECTION_LABELS: Record<NonNullable<ProtectionReason>, string> = {
  Active: '執行中／啟動中',
  ServiceContainer: '部署服務（不回收）',
  NotReclaimable: '沒有回收允許標記',
  Excluded: '明確排除',
  WorkspaceUnverified: '無法確認 workspace 外置',
  Referenced: '有 container 使用',
  Pinned: '正式設定使用中',
  RecentVersion: '最近版本（保留供回復）',
  ProtectedVersion: '保護的回復版本',
  ForeignTag: '有其他專案的 tag',
  OwnershipUnproven: '無法證明屬於 Ymir',
  StateUnknown: '狀態無法確認（保護）',
};

/** 回收資格：保護原因、可依期限回收，或只在資源不足時可回收。 */
export function eligibilityLabel(item: ContainerResource | ImageResource): string {
  if (item.protection) return PROTECTION_LABELS[item.protection] ?? '保護';
  return item.retentionEligible ? '可回收（已超過保留期限）' : '可回收（資源不足時）';
}

export function isEligible(item: ContainerResource | ImageResource): boolean {
  return !item.protection;
}

export function reclaimStateLabel(state: ReclaimState): string {
  switch (state) {
    case 'Active':
      return '自動回收運作中';
    case 'LocksUnavailable':
      return '已開啟，但生命週期鎖無法使用：只提供監控與 dry run';
    default:
      return '自動回收已關閉';
  }
}

export function dataStatusLabel(status: DataStatus): string {
  switch (status) {
    case 'Available':
      return '正常';
    case 'Stale':
      return '資料過期';
    case 'Unsupported':
      return '不支援';
    default:
      return '查詢失敗';
  }
}

export function triggerLabel(trigger: ReclaimTrigger): string {
  switch (trigger) {
    case 'Cpu':
      return 'CPU 剩餘不足';
    case 'Memory':
      return '記憶體不足';
    case 'Disk':
      return '磁碟不足';
    default:
      return '保留天數';
  }
}

/** 門檻代號（cpu / memory / disk:fs-xxxx）的顯示名稱。 */
export function pressureKeyLabel(key: string): string {
  if (key === 'cpu') return 'CPU';
  if (key === 'memory') return '記憶體';
  return key.startsWith('disk:') ? `磁碟 ${key.slice(5)}` : key;
}

/** 檔案系統的實測可用空間變化（不以 image 尺寸推算）。 */
export function freedBytes(
  filesystems: readonly {
    availableBeforeBytes: number | string;
    availableAfterBytes: number | string;
  }[],
): number {
  return filesystems.reduce(
    (sum, f) =>
      sum + Math.max(0, (num(f.availableAfterBytes) ?? 0) - (num(f.availableBeforeBytes) ?? 0)),
    0,
  );
}

/** 表單草稿：輸入框的字串。 */
export interface ReclamationDraft {
  enabled: boolean;
  containerRetentionDays: string;
  imageRetentionDays: string;
  cpuMinFreePercent: string;
  memoryMinAvailableGb: string;
  memoryMinAvailablePercent: string;
  diskMinAvailableGb: string;
  diskMinAvailablePercent: string;
}

export function reclamationDraftFrom(policy: ReclamationPolicy): ReclamationDraft {
  const text = (v: number | string) => String(num(v) ?? 0);
  return {
    enabled: policy.enabled,
    containerRetentionDays: text(policy.containerRetentionDays),
    imageRetentionDays: text(policy.imageRetentionDays),
    cpuMinFreePercent: text(policy.cpuMinFreePercent),
    memoryMinAvailableGb: text(policy.memoryMinAvailableGb),
    memoryMinAvailablePercent: text(policy.memoryMinAvailablePercent),
    diskMinAvailableGb: text(policy.diskMinAvailableGb),
    diskMinAvailablePercent: text(policy.diskMinAvailablePercent),
  };
}

/** 與後端 ReclamationPolicy.Validate 相同的範圍；不合法時回傳訊息。 */
export function reclamationDraftProblem(draft: ReclamationDraft): string | null {
  const days = [draft.containerRetentionDays, draft.imageRetentionDays].map((v) =>
    v.trim() === '' ? NaN : Number(v),
  );
  if (days.some((d) => !Number.isInteger(d) || d < 0 || d > 3650)) {
    return '保留天數必須是 0～3650 的整數（0 代表不依期限回收）。';
  }
  const percents = [
    draft.cpuMinFreePercent,
    draft.memoryMinAvailablePercent,
    draft.diskMinAvailablePercent,
  ].map((v) => (v.trim() === '' ? NaN : Number(v)));
  if (percents.some((p) => !Number.isFinite(p) || p < 0 || p > 99)) {
    return '比例門檻必須在 0～99 之間（0 代表停用）。';
  }
  const gbs = [draft.memoryMinAvailableGb, draft.diskMinAvailableGb].map((v) =>
    v.trim() === '' ? NaN : Number(v),
  );
  if (gbs.some((g) => !Number.isFinite(g) || g < 0 || g > 100000)) {
    return '容量門檻必須在 0～100000 GB 之間（0 代表停用）。';
  }
  return null;
}

export function toReclamationPolicy(draft: ReclamationDraft): ReclamationPolicy {
  return {
    enabled: draft.enabled,
    containerRetentionDays: Number(draft.containerRetentionDays),
    imageRetentionDays: Number(draft.imageRetentionDays),
    cpuMinFreePercent: Number(draft.cpuMinFreePercent),
    memoryMinAvailableGb: Number(draft.memoryMinAvailableGb),
    memoryMinAvailablePercent: Number(draft.memoryMinAvailablePercent),
    diskMinAvailableGb: Number(draft.diskMinAvailableGb),
    diskMinAvailablePercent: Number(draft.diskMinAvailablePercent),
  };
}

/** 政策摘要：讓管理員一眼看出會做什麼（0 天不是立即刪除）。 */
export function reclamationSummary(policy: ReclamationPolicy): string {
  if (!policy.enabled) return '自動回收已關閉：只監控，不會刪除任何東西。';
  const parts: string[] = [];
  const c = num(policy.containerRetentionDays) ?? 0;
  const i = num(policy.imageRetentionDays) ?? 0;
  parts.push(c > 0 ? `停止的 Agent 執行環境 ${c} 天後回收` : '執行環境不依期限回收');
  parts.push(i > 0 ? `未使用的 image ${i} 天後回收` : 'image 不依期限回收');
  const thresholds = [
    (num(policy.cpuMinFreePercent) ?? 0) > 0 ? `CPU 剩餘 < ${policy.cpuMinFreePercent}%` : null,
    (num(policy.memoryMinAvailableGb) ?? 0) > 0
      ? `記憶體 < ${policy.memoryMinAvailableGb} GB`
      : null,
    (num(policy.memoryMinAvailablePercent) ?? 0) > 0
      ? `記憶體 < ${policy.memoryMinAvailablePercent}%`
      : null,
    (num(policy.diskMinAvailableGb) ?? 0) > 0 ? `磁碟 < ${policy.diskMinAvailableGb} GB` : null,
    (num(policy.diskMinAvailablePercent) ?? 0) > 0
      ? `磁碟 < ${policy.diskMinAvailablePercent}%`
      : null,
  ].filter((t): t is string => t !== null);
  parts.push(
    thresholds.length > 0 ? `資源不足時緊急回收（${thresholds.join('、')}）` : '未設定資源門檻',
  );
  return parts.join('；') + '。';
}

/**
 * 資源不足告警（ADR-0025 §3「無安全候選或資源仍不足時顯示告警」）。
 * 只看低於門檻的項目：自動回收沒有運作時提醒不會自動處理；已觸發緊急回收卻仍不足時，
 * 說明是否還有安全候選，並明示 Ymir 不會停止執行中的工作來達到門檻。還在等待持續時間的項目不告警。
 */
export function pressureAlerts(snapshot: ResourceSnapshot): { key: string; message: string }[] {
  const alerts: { key: string; message: string }[] = [];
  for (const p of snapshot.pressure) {
    if (!p.below) continue;
    const label = pressureKeyLabel(p.key);
    if (snapshot.reclaimState !== 'Active') {
      alerts.push({
        key: p.key,
        message: `${label}低於門檻，但${reclaimStateLabel(snapshot.reclaimState)}，不會自動處理；請人工檢查。`,
      });
      continue;
    }
    if (!p.triggered) continue;

    // 磁碟只看位於受壓檔案系統上的 store；CPU / 記憶體看全部（與維護服務的候選範圍相同）。
    const stores = p.key.startsWith('disk:')
      ? (snapshot.filesystems.find((f) => f.id === p.key.slice(5))?.stores ?? [])
      : null;
    const inScope = (item: ContainerResource | ImageResource) =>
      stores === null || stores.includes(item.store);
    const remaining = [...snapshot.containers, ...snapshot.images].filter(
      (i) => isEligible(i) && inScope(i),
    ).length;
    const tail =
      p.key === 'cpu' || p.key === 'memory'
        ? 'Ymir 不會停止執行中的工作來達到門檻，請人工檢查主機負載。'
        : '請人工檢查磁碟用量（Ymir 不刪 volume、workspace 或受保護的 image）。';
    alerts.push({
      key: p.key,
      message:
        remaining === 0
          ? `${label}在緊急回收後仍低於門檻，且已沒有可安全回收的項目。${tail}`
          : `${label}在緊急回收後仍低於門檻；還有 ${remaining} 個可回收項目，會在下一輪繼續。${tail}`,
    });
  }
  return alerts;
}
