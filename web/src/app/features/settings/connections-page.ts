import { ChangeDetectionStrategy, Component } from '@angular/core';
import { OneDriveCard } from './onedrive-card';

/** 個人設定「帳號連接」分頁（ADR-0013、ADR-0028）。OneDrive 授權導回也落在這裡（?onedrive=結果）。 */
@Component({
  selector: 'app-connections-page',
  imports: [OneDriveCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section aria-label="帳號連接" class="stack">
      <p class="muted">
        只提供平台已支援的服務；可用能力仍由管理員控制。授權帳號不會自動把整個雲端交給
        Agent；請在對話檔案面板明確選取項目。任意連結項目目前只下載，不寫回原位置。
      </p>
      <app-onedrive-card />
    </section>
  `,
  styles: `
    :host {
      display: block;
    }
    .stack {
      gap: 1.5rem;
    }
    p {
      margin: 0;
    }
  `,
})
export class ConnectionsPage {}
