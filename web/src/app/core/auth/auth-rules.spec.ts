import { AdminUser, Me } from '../api/api-types';
import {
  assignableRoles,
  canToggleUser,
  canUseMessaging,
  describeRole,
  describeAuthMethod,
  isLocalPath,
  loginErrorMessage,
  newPasswordProblem,
  googleLoginUrl,
  oidcLoginUrl,
} from './auth-rules';

describe('auth rules', () => {
  it('maps login error codes to messages', () => {
    expect(loginErrorMessage('disabled')).toContain('停用');
    expect(loginErrorMessage('failed')).toContain('失敗');
    expect(loginErrorMessage('not_allowed')).toContain('Google');
    expect(loginErrorMessage(null)).toBeNull();
    expect(loginErrorMessage('other')).toBeNull();
  });

  it('only accepts local return paths', () => {
    expect(isLocalPath('/c/1')).toBe(true);
    expect(isLocalPath('//evil.example')).toBe(false);
    expect(isLocalPath('/\\evil.example')).toBe(false);
    expect(isLocalPath('https://evil.example')).toBe(false);
    expect(isLocalPath(null)).toBe(false);
  });

  it('builds the OIDC login url with an encoded local return path', () => {
    expect(oidcLoginUrl('/c/abc?x=1')).toBe('/api/auth/login?returnUrl=%2Fc%2Fabc%3Fx%3D1');
    expect(oidcLoginUrl('https://evil.example')).toBe('/api/auth/login?returnUrl=%2F');
    expect(oidcLoginUrl(undefined)).toBe('/api/auth/login?returnUrl=%2F');
  });

  it('builds the Google login url with an encoded local return path', () => {
    expect(googleLoginUrl('/c/abc')).toBe('/api/auth/login/google?returnUrl=%2Fc%2Fabc');
    expect(googleLoginUrl('//evil.example')).toBe('/api/auth/login/google?returnUrl=%2F');
  });

  it('describes auth methods', () => {
    expect(describeAuthMethod('Oidc')).toBe('企業帳號');
    expect(describeAuthMethod('Local')).toBe('本機帳號');
    expect(describeAuthMethod('Dev')).toBe('開發登入');
    expect(describeAuthMethod('Google')).toBe('Google 帳號');
  });

  it('does not let admins disable themselves', () => {
    const me = { id: 'me' } as Me;
    expect(canToggleUser({ id: 'me' } as AdminUser, me)).toBe(false);
    expect(canToggleUser({ id: 'other' } as AdminUser, me)).toBe(true);
    expect(canToggleUser({ id: 'other' } as AdminUser, null)).toBe(false);
  });

  it('describes roles', () => {
    expect(describeRole('Admin')).toBe('管理員');
    expect(describeRole('User')).toBe('使用者');
    expect(describeRole('Guest')).toBe('訪客');
  });

  it('limits role changes for self and IdP-managed admins', () => {
    const me = { id: 'me' } as Me;
    const local = { id: 'a', role: 'User', canChangeAdminRole: true } as AdminUser;
    const idpUser = { id: 'b', role: 'Guest', canChangeAdminRole: false } as AdminUser;
    const idpAdmin = { id: 'c', role: 'Admin', canChangeAdminRole: false } as AdminUser;
    expect(assignableRoles(local, me)).toEqual(['User', 'Guest', 'Admin']);
    expect(assignableRoles(idpUser, me)).toEqual(['User', 'Guest']);
    expect(assignableRoles(idpAdmin, me)).toEqual([]);
    expect(assignableRoles({ ...local, id: 'me' }, me)).toEqual([]);
    expect(assignableRoles(local, null)).toEqual([]);
  });

  it('hides messaging from guests', () => {
    expect(canUseMessaging({ role: 'User' } as Me)).toBe(true);
    expect(canUseMessaging({ role: 'Admin' } as Me)).toBe(true);
    expect(canUseMessaging({ role: 'Guest' } as Me)).toBe(false);
    expect(canUseMessaging(null)).toBe(false);
  });

  it('validates new passwords', () => {
    expect(newPasswordProblem('short', 'short')).toContain('12');
    expect(newPasswordProblem('            ', '            ')).toContain('12');
    expect(newPasswordProblem('long-enough-pw', 'different-pw!')).toContain('不一致');
    expect(newPasswordProblem('same-password-1', 'same-password-1', 'same-password-1')).toContain(
      '相同',
    );
    expect(newPasswordProblem('long-enough-pw', 'long-enough-pw', 'old-password-1')).toBeNull();
  });
});
