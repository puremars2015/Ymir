import { describe, expect, it } from 'vitest';
import { canRegisterServiceWorker, installHint, isIosDevice } from './pwa-rules';

const IPHONE =
  'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1';
const MAC_SAFARI =
  'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Version/18.0 Safari/605.1.15';
const ANDROID =
  'Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 Chrome/130.0 Mobile Safari/537.36';

describe('pwa-rules', () => {
  it('已用主畫面圖示開啟時不再提示', () => {
    expect(installHint({ userAgent: IPHONE, standalone: true, hasInstallPrompt: false })).toBe(
      'installed',
    );
  });

  it('Android 取得安裝事件時顯示安裝按鈕', () => {
    expect(installHint({ userAgent: ANDROID, standalone: false, hasInstallPrompt: true })).toBe(
      'prompt',
    );
  });

  it('iPhone 顯示手動加入主畫面的說明', () => {
    expect(installHint({ userAgent: IPHONE, standalone: false, hasInstallPrompt: false })).toBe(
      'ios',
    );
  });

  it('iPadOS 偽裝成 Mac 時以觸控點數判斷', () => {
    expect(isIosDevice({ userAgent: MAC_SAFARI, maxTouchPoints: 5 })).toBe(true);
    expect(isIosDevice({ userAgent: MAC_SAFARI, maxTouchPoints: 0 })).toBe(false);
  });

  it('桌機且沒有安裝事件時不提示', () => {
    expect(installHint({ userAgent: MAC_SAFARI, standalone: false, hasInstallPrompt: false })).toBe(
      'none',
    );
  });

  it('service worker 只在 https 或 localhost 註冊', () => {
    expect(canRegisterServiceWorker('https:', 'ymir.example.com')).toBe(true);
    expect(canRegisterServiceWorker('http:', 'localhost')).toBe(true);
    expect(canRegisterServiceWorker('http:', 'ymir.example.com')).toBe(false);
  });
});
