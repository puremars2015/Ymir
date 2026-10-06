import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

/** 管理區上方的分頁連結。 */
@Component({
  selector: 'app-admin-tabs',
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav class="tabs" aria-label="管理">
      <a routerLink="/admin/users" routerLinkActive="active" ariaCurrentWhenActive="page"
        >使用者管理</a
      >
      <a routerLink="/admin/make-topics" routerLinkActive="active" ariaCurrentWhenActive="page"
        >Make 主題</a
      >
    </nav>
  `,
  styles: `
    .tabs {
      display: flex;
      gap: 0.25rem;
      border-bottom: 1px solid var(--border);
    }
    a {
      padding: 0.5rem 0.875rem;
      color: var(--text-muted);
      text-decoration: none;
      border-bottom: 2px solid transparent;
      margin-bottom: -1px;
    }
    a.active {
      color: var(--text);
      font-weight: 600;
      border-bottom-color: var(--accent);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminTabs {}
