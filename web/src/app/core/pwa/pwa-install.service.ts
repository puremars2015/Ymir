import { computed, Injectable, signal } from '@angular/core';
import { installHint } from './pwa-rules';

/** Chrome / Edge 的 beforeinstallprompt 事件（尚未進 lib.dom）。 */
interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>;
  readonly userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

/** 「安裝到手機」：Android 觸發原生安裝，iOS 顯示手動步驟。登入狀態仍是 HttpOnly cookie，這裡不碰任何憑證。 */
@Injectable({ providedIn: 'root' })
export class PwaInstallService {
  private readonly deferred = signal<BeforeInstallPromptEvent | null>(null);
  private readonly installed = signal(false);

  readonly hint = computed(() =>
    installHint({
      userAgent: navigator.userAgent,
      maxTouchPoints: navigator.maxTouchPoints,
      standalone:
        this.installed() ||
        window.matchMedia?.('(display-mode: standalone)').matches === true ||
        (navigator as Navigator & { standalone?: boolean }).standalone === true,
      hasInstallPrompt: this.deferred() !== null,
    }),
  );

  constructor() {
    window.addEventListener('beforeinstallprompt', (event) => {
      // 不讓瀏覽器自己的迷你資訊列跳出來，改由登入頁的按鈕觸發。
      event.preventDefault();
      this.deferred.set(event as BeforeInstallPromptEvent);
    });
    window.addEventListener('appinstalled', () => {
      this.deferred.set(null);
      this.installed.set(true);
    });
  }

  async install(): Promise<void> {
    const event = this.deferred();
    if (!event) return;
    await event.prompt();
    await event.userChoice;
    this.deferred.set(null);
  }
}
