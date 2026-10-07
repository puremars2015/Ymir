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
import { forkJoin, Observable } from 'rxjs';
import {
  CAPABILITIES,
  CapabilityKey,
  effectiveValue,
  GRANT_SETTINGS,
  grantLabel,
} from '../../core/admin/extension-rules';
import { ApiService, describeApiError } from '../../core/api/api.service';
import {
  AdminUser,
  ExtensionGrantSetting,
  ExtensionValues,
  SaveUserExtensionsRequest,
  UserRole,
} from '../../core/api/api-types';
import {
  canToggleUser,
  describeAuthMethod,
  newPasswordProblem,
  PASSWORD_MIN_LENGTH,
} from '../../core/auth/auth-rules';
import { AuthService } from '../../core/auth/auth.service';
import { AdminTabs } from './admin-tabs';

/**
 * 使用者管理（Admin，SA §4、ADR-0009）：停用 / 啟用帳號、建立本機帳號、重設本機帳號密碼、
 * 個人的 Agent 擴充能力覆寫（ADR-0012）。
 * 企業帳號的角色以 Entra 的 app role 為準，不在這裡修改。
 */
@Component({
  selector: 'app-users-page',
  imports: [FormsModule, DatePipe, AdminTabs],
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
                <option value="User">User</option>
                <option value="Admin">Admin</option>
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
                <td>{{ user.role === 'Admin' ? '管理員' : '使用者' }}</td>
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
                    @if (canToggle(user)) {
                      <button
                        type="button"
                        class="link"
                        [class.danger-text]="user.status !== 'Disabled'"
                        [disabled]="busy()"
                        (click)="toggle(user)"
                      >
                        {{ user.status === 'Disabled' ? '啟用' : '停用' }}
                      </button>
                    }
                    <button
                      type="button"
                      class="link"
                      [disabled]="busy()"
                      (click)="startExtensions(user)"
                    >
                      擴充能力
                    </button>
                    @if (user.authMethod === 'Local') {
                      <button
                        type="button"
                        class="link"
                        [disabled]="busy()"
                        (click)="startReset(user)"
                      >
                        重設密碼
                      </button>
                    }
                  </span>
                </td>
              </tr>
              @if (resetting()?.id === user.id) {
                <tr class="reset-row">
                  <td colspan="7">
                    <form class="inline-form" (ngSubmit)="reset(user)">
                      <input
                        name="reset"
                        type="password"
                        autocomplete="new-password"
                        placeholder="新的初始密碼（至少 {{ minLength }} 字元）"
                        [ngModel]="resetPassword()"
                        (ngModelChange)="resetPassword.set($event)"
                      />
                      <button
                        type="submit"
                        [disabled]="busy() || resetPassword().length < minLength"
                      >
                        重設
                      </button>
                      <button type="button" class="secondary" (click)="resetting.set(null)">
                        取消
                      </button>
                    </form>
                  </td>
                </tr>
              }
              @if (extensions()?.user?.id === user.id) {
                <tr class="reset-row">
                  <td colspan="7">
                    @if (extensions(); as e) {
                      <form class="stack extension-form" (ngSubmit)="saveExtensions()">
                        <div class="grid">
                          @for (c of capabilities; track c.key) {
                            <label
                              >{{ c.label }}
                              <select
                                [name]="c.key"
                                [attr.name]="c.key"
                                [ngModel]="e.draft[c.key]"
                                (ngModelChange)="patchExtension(c.key, $event)"
                              >
                                @for (g of grantSettings; track g) {
                                  <option [value]="g">{{ grantText(g, e.defaults[c.key]) }}</option>
                                }
                              </select>
                              <span class="muted small"
                                >結果：{{
                                  effective(e.draft[c.key], e.defaults[c.key]) ? '允許' : '不允許'
                                }}</span
                              >
                            </label>
                          }
                        </div>
                        <p class="muted small">從這位成員的下一則訊息開始生效。</p>
                        <div class="row">
                          <button type="submit" [disabled]="busy()">儲存</button>
                          <button type="button" class="secondary" (click)="extensions.set(null)">
                            取消
                          </button>
                        </div>
                      </form>
                    }
                  </td>
                </tr>
              }
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

  protected readonly resetting = signal<AdminUser | null>(null);
  protected readonly capabilities = CAPABILITIES;
  protected readonly grantSettings = GRANT_SETTINGS;
  /** 正在編輯擴充能力的成員：覆寫草稿與全域預設（用來顯示「依全域預設」的結果）。 */
  protected readonly extensions = signal<{
    user: AdminUser;
    draft: Required<SaveUserExtensionsRequest>;
    defaults: ExtensionValues;
  } | null>(null);
  protected readonly resetPassword = signal('');
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

  protected toggle(user: AdminUser): void {
    const enable = user.status === 'Disabled';
    if (
      !enable &&
      !confirm(`確定停用「${user.displayName}」？對方會立即被登出，執行中的工作也會停止。`)
    ) {
      return;
    }
    this.run(this.api.adminSetUserEnabled(user.id, enable), (updated) => {
      this.users.update((list) => list.map((u) => (u.id === updated.id ? updated : u)));
      this.message.set(`${updated.displayName} 已${enable ? '啟用' : '停用'}。`);
    });
  }

  protected startReset(user: AdminUser): void {
    this.resetPassword.set('');
    this.resetting.set(user);
  }

  protected reset(user: AdminUser): void {
    const problem = newPasswordProblem(this.resetPassword(), this.resetPassword());
    if (problem) {
      this.error.set(problem);
      return;
    }
    this.run(this.api.adminResetPassword(user.id, this.resetPassword()), () => {
      this.resetting.set(null);
      this.message.set(`已重設 ${user.accountName} 的密碼；對方下次登入時必須變更。`);
    });
  }

  protected grantText(setting: ExtensionGrantSetting, defaultAllowed: boolean): string {
    return grantLabel(setting, defaultAllowed);
  }

  protected effective(setting: ExtensionGrantSetting, defaultAllowed: boolean): boolean {
    return effectiveValue(setting, defaultAllowed);
  }

  protected startExtensions(user: AdminUser): void {
    this.run(
      forkJoin({
        state: this.api.adminGetUserExtensions(user.id),
        policy: this.api.adminGetExtensionPolicy(),
      }),
      ({ state, policy }) =>
        this.extensions.set({
          user,
          draft: {
            skills: state.skills,
            mcp: state.mcp,
            internet: state.internet,
            oneDrive: state.oneDrive,
          },
          defaults: policy.defaults,
        }),
    );
  }

  protected patchExtension(key: CapabilityKey, value: ExtensionGrantSetting): void {
    this.extensions.update((e) => (e ? { ...e, draft: { ...e.draft, [key]: value } } : e));
  }

  protected saveExtensions(): void {
    const editing = this.extensions();
    if (!editing) return;
    this.run(this.api.adminSaveUserExtensions(editing.user.id, editing.draft), () => {
      this.extensions.set(null);
      this.message.set(`已更新 ${editing.user.displayName} 的擴充能力，從下一則訊息開始生效。`);
    });
  }

  private run<T>(request: Observable<T>, done: (value: T) => void): void {
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
      },
    });
  }
}
