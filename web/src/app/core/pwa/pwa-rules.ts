/** 安裝到手機的提示類型（純函式，方便測試）。 */
export type InstallHint = 'installed' | 'prompt' | 'ios' | 'none';

export interface InstallEnv {
  readonly userAgent: string;
  /** 已經以獨立視窗（主畫面圖示）開啟。 */
  readonly standalone: boolean;
  /** 瀏覽器已提供 beforeinstallprompt（Android Chrome / Edge）。 */
  readonly hasInstallPrompt: boolean;
  /** iPadOS 的 Safari 會偽裝成 Mac，要靠觸控點數判斷。 */
  readonly maxTouchPoints?: number;
}

/** iOS / iPadOS 的 Safari 沒有安裝 API，只能請使用者用「分享 → 加入主畫面」。 */
export function isIosDevice(env: Pick<InstallEnv, 'userAgent' | 'maxTouchPoints'>): boolean {
  return (
    /iPhone|iPad|iPod/.test(env.userAgent) ||
    (/Macintosh/.test(env.userAgent) && (env.maxTouchPoints ?? 0) > 1)
  );
}

export function installHint(env: InstallEnv): InstallHint {
  if (env.standalone) return 'installed';
  if (env.hasInstallPrompt) return 'prompt';
  if (isIosDevice(env)) return 'ios';
  return 'none';
}

/** service worker 需要安全環境；localhost 例外（本機開發與 e2e）。 */
export function canRegisterServiceWorker(protocol: string, hostname: string): boolean {
  return protocol === 'https:' || hostname === 'localhost' || hostname === '127.0.0.1';
}
