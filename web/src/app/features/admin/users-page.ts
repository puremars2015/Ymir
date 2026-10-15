import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { AdminUser, UserRole } from '../../core/api/api-types';
import {
  canToggleUser,
  describeAuthMethod,
  describeRole,
  PASSWORD_MIN_LENGTH,
} from '../../core/auth/auth-rules';
import { AuthService } from '../../core/auth/auth.service';
import { AdminTabs } from './admin-tabs';

/**
 * 使用者管理（Admin，SA §4、ADR-0009、ADR-0027）：清單、搜尋、建立本機帳號。
 * 每列只放「編輯」與「刪除」，避免列太長；顯示名稱、角色、停用 / 啟用、重設密碼、
 * 擴充能力（ADR-0012）與模型權限都在「編輯」進入的使用者設定頁（`UserDetailPage`）。
 */
@Component({
  selector: 'app-users-page',
  imports: [FormsModule, DatePipe, RouterLink, AdminTabs],
  template: `
    <section class="admin">
      <app-admin-tabs />
      <header class="head">
        <h1>使用者管理</h1>
        <button type="button" (click)="toggleCreate()">
          {{ creating() ? '取消' : '新增本機帳號' }}
        </button>
      </header>

      @if (creating()) {
        <form class="card stack create" (ngSubmit)="create()">
          <h2>新增本機帳號</h2>
          <p class="muted hint">給沒有公司帳號的人使用。對方第一次登入時必須改掉這個初始密碼。</p>
          <div class="grid">
            <label
              >帳號
              <input
                name="account"
                [ngModel]="newAccount()"
                (ngModelChange)="newAccount.set($event)"
                required
            /></label>
            <label
              >顯示名稱
              <input
                name="displayName"
                [ngModel]="newName()"
                (ngModelChange)="newName.set($event)"
                required
            /></label>
            <label
              >Email（選填）
              <input
                name="email"
                type="email"
                [ngModel]="newEmail()"
                (ngModelChange)="newEmail.set($event)"
            /></label>
            <label>
              角色
              <select name="role" [ngModel]="newRole()" (ngModelChange)="newRole.set($event)">
                <option value="User">使用者</option>
                <option value="Guest">訪客（不能使用訊息與群組）</option>
                <option value="Admin">管理員</option>
              </select>
            </label>
            <label
              >初始密碼（至少 {{ minLength }} 字元）
              <input
                name="password"
                type="password"
                autocomplete="new-password"
                [ngModel]="newPassword()"
                (ngModelChange)="newPassword.set($event)"
                required
              />
            </label>
          </div>
          <div class="row">
            <button
              type="submit"
              [disabled]="
                busy() ||
                !newAccount().trim() ||
                !newName().trim() ||
                newPassword().length < minLength
              "
            >
              建立
            </button>
          </div>
        </form>
      }

      <form class="search" (ngSubmit)="load()">
        <input
          name="search"
          type="search"
          placeholder="搜尋名稱、帳號或 email"
          [ngModel]="search()"
          (ngModelChange)="search.set($event)"
        />
        <button type="submit" class="secondary">搜尋</button>
      </form>

      @if (message()) {
        <p class="muted" role="status">{{ message() }}</p>
      }
      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }

      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>名稱</th>
              <th>帳號</th>
              <th>登入方式</th>
              <th>角色</th>
              <th>狀態</th>
              <th>最後登入</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            @for (user of users(); track user.id) {
              <tr [class.disabled]="user.status === 'Disabled'">
                <td>
                  {{ user.displayName }}
                  @if (user.email && user.email !== user.accountName) {
                    <br /><span class="muted small">{{ user.email }}</span>
                  }
                </td>
                <td>{{ user.accountName }}</td>
                <td>{{ methodLabel(user) }}</td>
                <td>{{ roleLabel(user.role) }}</td>
                <td>
                  @if (user.status === 'Disabled') {
                    <span class="danger-text">已停用</span>
                  } @else {
                    啟用中
                  }
                </td>
                <td class="small">
                  {{ user.lastLoginAt ? (user.lastLoginAt | date: 'yyyy-MM-dd HH:mm') : '—' }}
                </td>
                <td>
                  <span class="actions">
                    <a class="edit" [routerLink]="['/admin/users', user.id]">編輯</a>
                    @if (canToggle(user)) {
                      <button
                        type="button"
                        class="link danger-text"
                        [disabled]="busy()"
                        (click)="remove(user)"
                      >
                        刪除
                      </button>
                    }
                  </span>
                </td>
              </tr>
            } @empty {
              <tr>
                <td colspan="7" class="muted">{{ loading() ? '載入中…' : '沒有符合的使用者' }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </section>
  `,
  styles: `
    :host {
      flex: 1;
      overflow-y: auto;
      padding: 2rem 1rem;
    }
    .admin {
      max-width: 64rem;
      margin: 0 auto;
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }
    .head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
    }
    h1 {
      margin: 0;
    }
    h2 {
      margin: 0;
      font-size: 1.125rem;
    }
    .hint,
    .small {
      font-size: 0.8125rem;
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr));
      gap: 0.75rem;
    }
    .grid label {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    .row {
      display: flex;
      gap: 0.75rem;
    }
    .search {
      display: flex;
      gap: 0.5rem;
    }
    .search input {
      flex: 1;
    }
    .table-wrap {
      overflow-x: auto;
    }
    table {
      width: 100%;
      border-collapse: collapse;
      font-size: 0.9375rem;
    }
    th,
    td {
      text-align: left;
      padding: 0.5rem 0.625rem;
      border-bottom: 1px solid var(--border);
      vertical-align: top;
    }
    th {
      color: var(--text-muted);
      font-weight: 600;
      font-size: 0.8125rem;
    }
    tr.disabled td {
      opacity: 0.65;
    }
    .actions {
      white-space: nowrap;
      display: inline-flex;
      gap: 0.75rem;
    }
    .actions a {
      text-decoration: none;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsersPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  protected readonly minLength = PASSWORD_MIN_LENGTH;

  protected readonly users = signal<AdminUser[]>([]);
  protected readonly search = signal('');
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);

  protected readonly creating = signal(false);
  protected readonly newAccount = signal('');
  protected readonly newName = signal('');
  protected readonly newEmail = signal('');
  protected readonly newRole = signal<UserRole>('User');
  protected readonly newPassword = signal('');

  private readonly me = computed(() => this.auth.user());

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.api.adminListUsers(this.search()).subscribe({
      next: (users) => {
        this.users.set(users);
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.loading.set(false);
      },
    });
  }

  protected methodLabel(user: AdminUser): string {
    return describeAuthMethod(user.authMethod);
  }

  protected canToggle(user: AdminUser): boolean {
    return canToggleUser(user, this.me());
  }

  protected roleLabel(role: UserRole): string {
    return describeRole(role);
  }

  protected remove(user: AdminUser): void {
    if (
      !confirm(
        `確定刪除「${user.displayName}」？對方會立即被登出、無法再登入，執行中的工作也會停止。對話與檔案會保留，但不會再出現在清單中。`,
      )
    ) {
      return;
    }
    this.run(this.api.adminDeleteUser(user.id), () => {
      this.users.update((list) => list.filter((u) => u.id !== user.id));
      this.message.set(`已刪除 ${user.displayName}。`);
    });
  }

  protected toggleCreate(): void {
    this.creating.update((v) => !v);
    this.newAccount.set('');
    this.newName.set('');
    this.newEmail.set('');
    this.newRole.set('User');
    this.newPassword.set('');
  }

  protected create(): void {
    this.run(
      this.api.adminCreateLocalUser({
        account: this.newAccount().trim(),
        displayName: this.newName().trim(),
        email: this.newEmail().trim() || null,
        role: this.newRole(),
        initialPassword: this.newPassword(),
      }),
      (user) => {
        this.creating.set(false);
        this.users.update((list) => [user, ...list]);
        this.message.set(`已建立本機帳號 ${user.accountName}，請把初始密碼交給對方。`);
      },
    );
  }

  private run<T>(request: Observable<T>, done: (value: T) => void, failed?: () => void): void {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    request.subscribe({
      next: (value) => {
        done(value);
        this.busy.set(false);
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.busy.set(false);
        failed?.();
      },
    });
  }
}
