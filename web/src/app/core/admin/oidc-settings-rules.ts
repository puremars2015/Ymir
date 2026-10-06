import { OidcSettings, OidcSettingsSource, SaveOidcSettingsRequest } from '../api/api-types';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const IDENTIFIER = /^[A-Za-z0-9._-]{1,100}$/;

/** 到期前幾天開始提醒。 */
export const SECRET_WARNING_DAYS = 30;

export interface OidcDraft {
  enabled: boolean;
  tenantId: string;
  clientId: string;
  /** 留空表示不變更 */
  clientSecret: string;
  /** `yyyy-MM-dd` 或空字串 */
  secretExpiresOn: string;
  adminRole: string;
  displayName: string;
}

export function oidcDraftFrom(settings: OidcSettings): OidcDraft {
  return {
    enabled: settings.source === 'None' ? true : settings.enabled,
    tenantId: settings.tenantId ?? '',
    clientId: settings.clientId ?? '',
    clientSecret: '',
    secretExpiresOn: settings.secretExpiresOn ?? '',
    adminRole: settings.adminRole,
    displayName: settings.displayName,
  };
}

/** 與後端相同的檢查（後端才是權威，這裡只為了即時提示）。 */
export function oidcDraftProblem(draft: OidcDraft, hasClientSecret: boolean): string | null {
  if (!GUID.test(draft.tenantId.trim())) return 'Tenant ID 必須是 GUID（目錄識別碼）';
  if (!IDENTIFIER.test(draft.clientId.trim())) return 'Client ID 格式不正確';
  if (!draft.adminRole.trim() || /\s/.test(draft.adminRole.trim())) {
    return 'Admin 角色不可空白或含空白字元';
  }
  const name = draft.displayName.trim();
  if (!name || name.length > 30) return '登入按鈕名稱必須是 1～30 個字';
  if (draft.clientSecret.length > 500) return 'Client secret 太長';
  if (draft.enabled && !hasClientSecret && !draft.clientSecret) return '請輸入 client secret';
  return null;
}

export function toSaveOidcRequest(draft: OidcDraft): SaveOidcSettingsRequest {
  return {
    enabled: draft.enabled,
    tenantId: draft.tenantId.trim(),
    clientId: draft.clientId.trim(),
    clientSecret: draft.clientSecret || null,
    secretExpiresOn: draft.secretExpiresOn || null,
    adminRole: draft.adminRole.trim(),
    displayName: draft.displayName.trim(),
  };
}

export type SecretExpiry =
  | { state: 'unknown' }
  | { state: 'ok' | 'soon'; days: number }
  | { state: 'expired'; days: number };

/** client secret 到期狀態；`today` 為當地日期 `yyyy-MM-dd`。 */
export function secretExpiry(expiresOn: string | null, today: string): SecretExpiry {
  if (!expiresOn) return { state: 'unknown' };
  const days = Math.round((Date.parse(expiresOn) - Date.parse(today)) / 86_400_000);
  if (Number.isNaN(days)) return { state: 'unknown' };
  if (days < 0) return { state: 'expired', days: -days };
  return { state: days <= SECRET_WARNING_DAYS ? 'soon' : 'ok', days };
}

/** 當地日期 `yyyy-MM-dd`。 */
export function localToday(date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

const SOURCE_LABELS: Record<OidcSettingsSource, string> = {
  None: '未設定',
  Deployment: '部署設定（.env）',
  Database: '管理介面',
};

export const oidcSourceLabel = (source: OidcSettingsSource): string => SOURCE_LABELS[source];

export interface AdminWarning {
  level: 'warn' | 'danger';
  message: string;
  link: string;
}

/** 總覽頁的警告。 */
export function oidcWarnings(settings: OidcSettings, today: string): AdminWarning[] {
  const link = '/admin/settings';
  if (!settings.configured) {
    return [
      {
        level: 'warn',
        message: '尚未設定企業帳號（Entra ID）登入，目前只能用本機帳號登入。',
        link,
      },
    ];
  }
  const expiry = secretExpiry(settings.secretExpiresOn, today);
  if (expiry.state === 'expired') {
    return [
      {
        level: 'danger',
        message: `Entra client secret 已過期 ${expiry.days} 天，企業帳號將無法登入。`,
        link,
      },
    ];
  }
  if (expiry.state === 'soon') {
    return [
      {
        level: 'warn',
        message: `Entra client secret 將在 ${expiry.days} 天後到期，請先在 Entra 產生新的 secret 並更新。`,
        link,
      },
    ];
  }
  return [];
}
