import { TunnelSettings } from '../api/api-types';
import {
  extractTunnelToken,
  hostnameProblem,
  tunnelState,
  tunnelTokenProblem,
  tunnelWarnings,
} from './tunnel-settings-rules';

const settings = (overrides: Partial<TunnelSettings> = {}): TunnelSettings => ({
  publicEdgeEnabled: true,
  hostname: 'ymir.example.com',
  hostnameSource: 'Deployment',
  managementAvailable: true,
  configured: true,
  active: true,
  tokenUpdatedAt: '2026-10-06T00:00:00Z',
  redirectUri: 'https://ymir.example.com/signin-oidc',
  ...overrides,
});

const token = 'eyJhIjoi' + 'A'.repeat(150) + 'fQ==';

describe('tunnel settings rules', () => {
  it('validates tokens like the server', () => {
    expect(tunnelTokenProblem(token)).toBeNull();
    expect(tunnelTokenProblem('')).toContain('貼上');
    expect(tunnelTokenProblem('short')).toContain('格式');
    expect(tunnelTokenProblem(token + ' extra')).toContain('格式');
  });

  it('extracts the token from a pasted cloudflared command', () => {
    expect(extractTunnelToken(`cloudflared service install ${token}`)).toBe(token);
    expect(extractTunnelToken(`cloudflared tunnel run --token ${token}`)).toBe(token);
    expect(extractTunnelToken(`  ${token}  `)).toBe(token);
  });

  it('validates host names', () => {
    expect(hostnameProblem('')).toBeNull();
    expect(hostnameProblem('ymir.thetainformation.com')).toBeNull();
    expect(hostnameProblem('https://ymir.example.com')).not.toBeNull();
    expect(hostnameProblem('ymir.example.com/path')).not.toBeNull();
    expect(hostnameProblem('localhost')).not.toBeNull();
    expect(hostnameProblem('-bad.example.com')).not.toBeNull();
  });

  it('derives the connection state and warnings', () => {
    expect(tunnelState(settings())).toBe('connected');
    expect(tunnelState(settings({ active: false }))).toBe('disconnected');
    expect(tunnelState(settings({ configured: false, active: false }))).toBe('not-configured');
    expect(tunnelState(settings({ managementAvailable: false }))).toBe('unavailable');
    expect(tunnelWarnings(settings({ active: false }))[0].level).toBe('danger');
    expect(tunnelWarnings(settings())).toEqual([]);
    expect(tunnelWarnings(settings({ managementAvailable: false }))).toEqual([]);
  });
});
