import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

/** 管理區上方的分頁連結。 */
@Component({
  selector: 'app-admin-tabs',
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav class="tabs" aria-label="管理">
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
  `,
  styles: `
    .tabs {
      display: flex;
      gap: 0.25rem;
      border-bottom: 1px solid var(--border);
      overflow-x: auto;
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
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminTabs {
  protected readonly tabs = [
    { path: '/admin', label: '總覽', exact: true },
    { path: '/admin/users', label: '使用者', exact: false },
    { path: '/admin/make-topics', label: 'Make 主題', exact: false },
    { path: '/admin/settings', label: '系統設定', exact: false },
    { path: '/admin/audit', label: '稽核紀錄', exact: false },
  ];
}
