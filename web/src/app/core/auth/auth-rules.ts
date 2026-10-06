import { AdminUser, AuthMethod, Me } from '../api/api-types';

/** 與後端 `LocalAccounts.MinimumPasswordLength` 一致（ADR-0009）。 */
export const PASSWORD_MIN_LENGTH = 12;

/** 登入頁 `?error=` 的訊息（OIDC callback 失敗時由後端導回）。 */
export function loginErrorMessage(code: string | null | undefined): string | null {
  switch (code) {
    case 'disabled':
      return '帳號已停用，請聯絡管理員。';
    case 'failed':
      return '企業帳號登入失敗，請再試一次。';
    default:
      return null;
  }
}

/** 只接受站內相對路徑（後端也會再檢查一次）。 */
export function isLocalPath(url: string | null | undefined): url is string {
  return !!url && url.startsWith('/') && !url.startsWith('//') && !url.startsWith('/\\');
}

/** 企業帳號登入：整頁導向後端，由後端完成 OIDC（ADR-0002、ADR-0009）。 */
export function oidcLoginUrl(returnUrl: string | null | undefined): string {
  const target = isLocalPath(returnUrl) ? returnUrl : '/';
  return `/api/auth/login?returnUrl=${encodeURIComponent(target)}`;
}

export function describeAuthMethod(method: AuthMethod): string {
  switch (method) {
    case 'Oidc':
      return '企業帳號';
    case 'Local':
      return '本機帳號';
    default:
      return '開發登入';
  }
}

/** Admin 不能停用自己。 */
export function canToggleUser(user: AdminUser, me: Me | null): boolean {
  return !!me && user.id !== me.id;
}

/** 新密碼的前端檢查（後端仍會檢查）；通過時回傳 null。 */
export function newPasswordProblem(
  newPassword: string,
  confirm: string,
  currentPassword?: string,
): string | null {
  if (newPassword.trim().length === 0 || newPassword.length < PASSWORD_MIN_LENGTH) {
    return `密碼至少 ${PASSWORD_MIN_LENGTH} 個字元。`;
  }
  if (currentPassword !== undefined && newPassword === currentPassword) {
    return '新密碼不能與目前密碼相同。';
  }
  if (newPassword !== confirm) {
    return '兩次輸入的密碼不一致。';
  }
  return null;
}
