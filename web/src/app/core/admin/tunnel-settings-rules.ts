import { TunnelSettings } from '../api/api-types';
import { AdminWarning } from './oidc-settings-rules';

/** 與後端 RuntimeHostProtocol.IsValidTunnelToken 相同（後端才是權威）。 */
export const TUNNEL_TOKEN_MIN = 100;
export const TUNNEL_TOKEN_MAX = 4096;
const TOKEN = /^[A-Za-z0-9+/=_.-]+$/;

export function tunnelTokenProblem(token: string): string | null {
  const value = token.trim();
  if (!value) return '請貼上 tunnel token';
  if (value.length < TUNNEL_TOKEN_MIN || value.length > TUNNEL_TOKEN_MAX || !TOKEN.test(value)) {
    return 'Token 格式不正確：請複製 Cloudflare 指令中 --token 後面那一長串';
  }
  return null;
}

/**
 * 從 `cloudflared service install <token>` 或 `cloudflared tunnel run --token <token>` 這類指令取出 token，
 * 讓管理員可以直接貼上 Cloudflare dashboard 給的整行指令。
 */
export function extractTunnelToken(input: string): string {
  const parts = input.trim().split(/\s+/);
  const flag = parts.indexOf('--token');
  if (flag >= 0 && parts[flag + 1]) return parts[flag + 1];
  return parts[parts.length - 1] ?? '';
}

const HOSTNAME =
  /^(?=.{1,253}$)(?!-)[a-z0-9-]{1,63}(?<!-)(\.(?!-)[a-z0-9-]{1,63}(?<!-))*\.[a-z][a-z0-9-]{0,62}$/i;

/** DNS 網域（不含 https://、port、路徑）；空字串代表還原為部署設定。 */
export function hostnameProblem(hostname: string): string | null {
  const value = hostname.trim();
  if (!value) return null;
  return HOSTNAME.test(value)
    ? null
    : '請輸入網域名稱，例如 ymir.example.com（不含 https:// 與路徑）';
}

export type TunnelState = 'unavailable' | 'not-configured' | 'connected' | 'disconnected';

export function tunnelState(settings: TunnelSettings): TunnelState {
  if (!settings.managementAvailable) return 'unavailable';
  if (!settings.configured) return 'not-configured';
  return settings.active ? 'connected' : 'disconnected';
}

const STATE_LABELS: Record<TunnelState, string> = {
  unavailable: '此部署不支援由網頁管理',
  'not-configured': '尚未設定 token',
  connected: '已連線',
  disconnected: '未連線',
};

export const tunnelStateLabel = (state: TunnelState): string => STATE_LABELS[state];

/** 總覽警告：已設定 token 但 cloudflared 沒有在執行。 */
export function tunnelWarnings(settings: TunnelSettings): AdminWarning[] {
  return tunnelState(settings) === 'disconnected'
    ? [
        {
          level: 'danger',
          message: 'Cloudflare Tunnel 未連線，外部使用者可能無法連到 Ymir。',
          link: '/admin/settings',
        },
      ]
    : [];
}
