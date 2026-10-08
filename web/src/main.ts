import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { canRegisterServiceWorker } from './app/core/pwa/pwa-rules';

bootstrapApplication(App, appConfig).catch((err) => console.error(err));

// PWA：讓手機可以「加入主畫面」（sw.js 只快取帶雜湊的靜態檔，不碰 /api）。
if (
  'serviceWorker' in navigator &&
  canRegisterServiceWorker(location.protocol, location.hostname)
) {
  window.addEventListener('load', () => {
    navigator.serviceWorker
      .register('/sw.js')
      .catch((err) => console.error('sw register failed', err));
  });
}
