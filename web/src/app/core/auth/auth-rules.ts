import { AdminUser, AuthMethod, Me, UserRole } from '../api/api-types';

/** 與後端 `LocalAccounts.MinimumPasswordLength` 一致（ADR-0009）。 */
export const PASSWORD_MIN_LENGTH = 12;

/** 登入頁 `?error=` 的訊息（企業帳號 / Google 的 callback 失敗時由後端導回）。 */
export function loginErrorMessage(code: string | null | undefined): string | null {
  switch (code) {
    case 'disabled':
      return '帳號已停用，請聯絡管理員。';
    case 'failed':
      return '登入失敗，請再試一次。';
    case 'not_allowed':
      return '此 Google 帳號未獲准使用，請改用公司網域的帳號或聯絡管理員。';
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

/** Google 帳號登入（ADR-0024）：同樣整頁導向後端。 */
export function googleLoginUrl(returnUrl: string | null | undefined): string {
  const target = isLocalPath(returnUrl) ? returnUrl : '/';
  return `/api/auth/login/google?returnUrl=${encodeURIComponent(target)}`;
}

export function describeAuthMethod(method: AuthMethod): string {
  switch (method) {
    case 'Oidc':
      return '企業帳號';
    case 'Local':
      return '本機帳號';
    case 'Google':
      return 'Google 帳號';
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

/** 角色的中文名稱（ADR-0027）。 */
export function describeRole(role: UserRole): string {
  switch (role) {
    case 'Admin':
      return '管理員';
    case 'Guest':
      return '訪客';
    default:
      return '使用者';
  }
}

/**
 * 管理員可以把這位使用者改成哪些角色（ADR-0027）；空陣列表示不能變更。
 * 不能改自己；企業帳號與 Google 帳號的管理員權限由 IdP 決定（`canChangeAdminRole`），只能在使用者與訪客之間切換。
 */
export function assignableRoles(user: AdminUser, me: Me | null): UserRole[] {
  if (!me || user.id === me.id) return [];
  if (user.canChangeAdminRole) return ['User', 'Guest', 'Admin'];
  return user.role === 'Admin' ? [] : ['User', 'Guest'];
}

/** 訪客不能使用使用者之間的訊息與群組聊天（ADR-0027）。 */
export function canUseMessaging(me: Me | null): boolean {
  return !!me && me.role !== 'Guest';
}
