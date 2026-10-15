import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

/** 個人設定的外框：標題與分頁列，各分頁是 /settings 底下的子路由，可以直接以網址開啟。 */
@Component({
  selector: 'app-settings-layout',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  template: `
    <section class="settings">
      <h1>個人設定</h1>
      @if (auth.user(); as user) {
        <p class="muted">{{ user.displayName }}{{ user.email ? ' · ' + user.email : '' }}</p>
      }
      <nav class="tabs" aria-label="個人設定">
        @for (tab of tabs; track tab.path) {
          <a
            [routerLink]="tab.path"
            routerLinkActive="active"
            [routerLinkActiveOptions]="{ exact: tab.exact }"
            ariaCurrentWhenActive="page"
            >{{ tab.label }}</a
          >
        }
      </nav>
      <div class="panel">
        <router-outlet />
      </div>
    </section>
  `,
  styles: `
    :host {
      flex: 1;
      overflow-y: auto;
      padding: 2rem 1rem;
    }
    .settings {
      max-width: 46rem;
      margin: 0 auto;
    }
    .tabs {
      display: flex;
      gap: 0.25rem;
      border-bottom: 1px solid var(--border);
      overflow-x: auto;
      margin-top: 1rem;
    }
    a {
      padding: 0.5rem 0.875rem;
      color: var(--text-muted);
      text-decoration: none;
      border-bottom: 2px solid transparent;
      margin-bottom: -1px;
      white-space: nowrap;
    }
    a.active {
      color: var(--text);
      font-weight: 600;
      border-bottom-color: var(--accent);
    }
    .panel {
      padding-top: 1.5rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SettingsLayout {
  protected readonly auth = inject(AuthService);
  protected readonly tabs = [
    { path: '/settings', label: '個人 System Prompt', exact: true },
    { path: '/settings/password', label: '變更密碼', exact: false },
    { path: '/settings/connections', label: '帳號連接', exact: false },
    { path: '/settings/tools', label: 'MCP 與工具', exact: false },
  ];
}
