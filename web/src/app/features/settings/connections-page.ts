import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { OneDriveCard } from './onedrive-card';
import { MyExtensionsCard } from './my-extensions-card';
import { PersonalMcpCard } from './personal-mcp-card';

@Component({
  selector: 'app-connections-page',
  imports: [RouterLink, OneDriveCard, MyExtensionsCard, PersonalMcpCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="connections stack">
      <a routerLink="/settings">← 個人設定</a>
      <h1>連接與工具</h1>
      <p>管理你自己的帳號連接與 MCP 工具；可用能力仍由管理員控制。</p>
      <section aria-label="帳號連接" class="stack">
        <h2>帳號連接（Connector）</h2>
        <p class="muted">
          只提供平台已支援的服務。授權帳號不會自動把整個雲端交給
          Agent；請在對話檔案面板明確選取項目。任意連結項目目前只下載，不寫回原位置。
        </p>
        <app-onedrive-card />
      </section>
      <app-personal-mcp-card />
      <app-my-extensions-card />
    </section>
  `,
  styles: `
    :host {
      flex: 1;
      overflow-y: auto;
      padding: 2rem 1rem;
    }
    .connections {
      max-width: 46rem;
      margin: 0 auto;
      gap: 1.5rem;
    }
    h1,
    h2,
    p {
      margin: 0;
    }
  `,
})
export class ConnectionsPage {}
