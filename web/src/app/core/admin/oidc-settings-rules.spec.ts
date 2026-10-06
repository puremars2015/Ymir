import { OidcSettings } from '../api/api-types';
import {
  localToday,
  oidcDraftFrom,
  oidcDraftProblem,
  oidcWarnings,
  secretExpiry,
  toSaveOidcRequest,
} from './oidc-settings-rules';

const tenant = 'e333846a-0ee2-4e2d-a13b-efc97851b892';

const settings = (overrides: Partial<OidcSettings> = {}): OidcSettings => ({
  source: 'Database',
  enabled: true,
  configured: true,
  tenantId: tenant,
  clientId: 'client',
  hasClientSecret: true,
  secretSource: 'Database',
  secretUpdatedAt: '2026-10-01T00:00:00Z',
  secretExpiresOn: '2027-01-31',
  adminRole: 'Ymir.Admin',
  displayName: '公司帳號',
  redirectUri: 'https://ymir.example.com/signin-oidc',
  updatedAt: null,
  updatedByName: null,
  ...overrides,
});

describe('oidc settings rules', () => {
  it('starts a new configuration enabled, with an empty secret field', () => {
    const draft = oidcDraftFrom(
      settings({ source: 'None', enabled: false, tenantId: null, clientId: null }),
    );
    expect(draft.enabled).toBe(true);
    expect(draft.tenantId).toBe('');
    expect(draft.clientSecret).toBe('');
  });

  it('validates the draft like the server', () => {
    const draft = oidcDraftFrom(settings());
    expect(oidcDraftProblem(draft, true)).toBeNull();
    expect(oidcDraftProblem({ ...draft, tenantId: 'contoso' }, true)).toContain('GUID');
    expect(oidcDraftProblem({ ...draft, clientId: 'has space' }, true)).toContain('Client ID');
    expect(oidcDraftProblem({ ...draft, adminRole: 'Ymir Admin' }, true)).toContain('角色');
    expect(oidcDraftProblem({ ...draft, displayName: '' }, true)).toContain('名稱');
    expect(oidcDraftProblem(draft, false)).toContain('client secret');
    expect(oidcDraftProblem({ ...draft, enabled: false }, false)).toBeNull();
  });

  it('sends null for an unchanged secret and an empty expiry', () => {
    const request = toSaveOidcRequest({
      ...oidcDraftFrom(settings()),
      clientSecret: '',
      secretExpiresOn: '',
      clientId: ' client ',
    });
    expect(request.clientSecret).toBeNull();
    expect(request.secretExpiresOn).toBeNull();
    expect(request.clientId).toBe('client');
  });

  it('classifies the secret expiry', () => {
    expect(secretExpiry(null, '2026-10-06')).toEqual({ state: 'unknown' });
    expect(secretExpiry('2027-01-31', '2026-10-06')).toEqual({ state: 'ok', days: 117 });
    expect(secretExpiry('2026-10-20', '2026-10-06')).toEqual({ state: 'soon', days: 14 });
    expect(secretExpiry('2026-10-01', '2026-10-06')).toEqual({ state: 'expired', days: 5 });
  });

  it('formats the local date', () => {
    expect(localToday(new Date(2026, 0, 5))).toBe('2026-01-05');
  });

  it('warns when Entra is missing or the secret is expiring', () => {
    expect(oidcWarnings(settings(), '2026-10-06')).toEqual([]);
    expect(oidcWarnings(settings({ configured: false }), '2026-10-06')[0].message).toContain(
      '尚未設定',
    );
    expect(oidcWarnings(settings({ secretExpiresOn: '2026-10-20' }), '2026-10-06')[0].level).toBe(
      'warn',
    );
    expect(oidcWarnings(settings({ secretExpiresOn: '2026-10-01' }), '2026-10-06')[0].level).toBe(
      'danger',
    );
  });
});
