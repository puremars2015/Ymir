import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
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
  assignableRoles,
  canToggleUser,
  describeAuthMethod,
  describeRole,
  newPasswordProblem,
  PASSWORD_MIN_LENGTH,
} from '../../core/auth/auth-rules';
import { AuthService } from '../../core/auth/auth.service';
import { AdminTabs } from './admin-tabs';
import { UserModelCard } from './user-model-card';

/**
 * 單一使用者的設定頁（Admin，ADR-0009、ADR-0012、ADR-0027）：使用者清單只留「編輯」與「刪除」，
 * 其餘操作（顯示名稱、角色、停用 / 啟用、重設密碼、擴充能力、模型權限）都集中在這裡，避免清單每列過長。
 * 企業帳號與 Google 帳號是否為管理員以 IdP 為準，角色只能在使用者與訪客之間切換（`assignableRoles`）。
 */
@Component({
  selector: 'app-user-detail-page',
  imports: [FormsModule, DatePipe, RouterLink, AdminTabs, UserModelCard],
  template: `
    <section class="admin">
      <app-admin-tabs />
      <a routerLink="/admin/users" class="back">← 返回使用者管理</a>

      @if (message()) {
        <p class="muted" role="status">{{ message() }}</p>
      }
      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }

      @if (user(); as u) {
        <header class="head">
          <div>
            <h1>{{ u.displayName }}</h1>
            <p class="muted small">
              {{ u.accountName }}
              @if (u.email && u.email !== u.accountName) {
                · {{ u.email }}
              }
              · {{ methodLabel(u) }} · 最後登入
              {{ u.lastLoginAt ? (u.lastLoginAt | date: 'yyyy-MM-dd HH:mm') : '—' }}
            </p>
          </div>
          @if (u.status === 'Disabled') {
            <span class="danger-text">已停用</span>
          } @else {
            <span class="muted">啟用中</span>
          }
        </header>

        <section class="card stack" aria-labelledby="basic-title">
          <h2 id="basic-title">基本資料</h2>
          <form class="inline-form rename-form" (ngSubmit)="rename(u)">
            <input
              name="displayName"
              aria-label="顯示名稱"
              maxlength="200"
              placeholder="顯示名稱"
              [ngModel]="renameText()"
              (ngModelChange)="renameText.set($event)"
            />
            <button
              type="submit"
              [disabled]="busy() || !renameText().trim() || renameText().trim() === u.displayName"
            >
              儲存名稱
            </button>
          </form>
          @if (u.authMethod === 'Oidc' || u.authMethod === 'Google') {
            <p class="muted small">設定後，對方登入時不再使用公司帳號系統的名稱。</p>
          }

          <div class="role">
            <span>角色</span>
            @if (roleOptions(u); as options) {
              @if (options.length > 0) {
                <select
                  class="role-select"
                  name="role"
                  [attr.aria-label]="u.displayName + ' 的角色'"
                  [disabled]="busy()"
                  (change)="changeRole(u, $event)"
                >
                  @for (role of options; track role) {
                    <option [value]="role" [selected]="role === u.role">
                      {{ roleLabel(role) }}
                    </option>
                  }
                </select>
              } @else {
                <span>{{ roleLabel(u.role) }}</span>
              }
            }
          </div>
        </section>

        @if (canToggle(u)) {
          <section class="card stack" aria-labelledby="status-title">
            <h2 id="status-title">帳號狀態</h2>
            <p class="muted small">停用後對方會立即被登出，執行中的工作也會停止。</p>
            <div class="row">
              <button
                type="button"
                class="toggle"
                [class.secondary]="u.status !== 'Disabled'"
                [class.danger-text]="u.status !== 'Disabled'"
                [disabled]="busy()"
                (click)="toggle(u)"
              >
                {{ u.status === 'Disabled' ? '啟用帳號' : '停用帳號' }}
              </button>
            </div>
          </section>
        }

        @if (u.authMethod === 'Local') {
          <section class="card stack" aria-labelledby="password-title">
            <h2 id="password-title">重設密碼</h2>
            <form class="inline-form reset-form" (ngSubmit)="reset(u)">
              <input
                name="reset"
                type="password"
                autocomplete="new-password"
                placeholder="新的初始密碼（至少 {{ minLength }} 字元）"
                [ngModel]="resetPassword()"
                (ngModelChange)="resetPassword.set($event)"
              />
              <button type="submit" [disabled]="busy() || resetPassword().length < minLength">
                重設
              </button>
            </form>
            <p class="muted small">對方下次登入時必須變更密碼。</p>
          </section>
        }

        <section class="card stack" aria-labelledby="extensions-title">
          <h2 id="extensions-title">擴充能力</h2>
          @if (extensions(); as e) {
            <form class="stack extension-form" (ngSubmit)="saveExtensions(u)">
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
                <button type="submit" [disabled]="busy()">儲存擴充能力</button>
              </div>
            </form>
          } @else {
            <p class="muted">載入中…</p>
          }
        </section>

        <section class="card">
          <!-- 模型權限卡片儲存或取消後會發出 closed；在設定頁裡改成重新載入卡片，而不是關閉。 -->
          @for (version of [modelVersion()]; track version) {
            <app-user-model-card
              [user]="u"
              (closed)="modelVersion.set(modelVersion() + 1)"
              (saved)="message.set($event)"
            />
          }
        </section>

        @if (canToggle(u)) {
          <section class="card stack danger-zone" aria-labelledby="delete-title">
            <h2 id="delete-title">刪除帳號</h2>
            <p class="muted small">
              對方會立即被登出、無法再登入。對話與檔案會保留，但不會再出現在清單中。
            </p>
            <div class="row">
              <button
                type="button"
                class="secondary danger-text delete"
                [disabled]="busy()"
                (click)="remove(u)"
              >
                刪除帳號
              </button>
            </div>
          </section>
        }
      } @else if (loading()) {
        <p class="muted">載入中…</p>
      }
    </section>
  `,
  styles: `
    :host {
      flex: 1;
      overflow-y: auto;
      padding: 2rem 1rem;
    }
    .admin {
      max-width: 48rem;
      margin: 0 auto;
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }
    .back {
      align-self: flex-start;
      font-size: 0.875rem;
    }
    .head {
      display: flex;
      align-items: flex-start;
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
    p {
      margin: 0;
    }
    .small {
      font-size: 0.8125rem;
    }
    .inline-form {
      margin-bottom: 0;
    }
    .role {
      display: flex;
      align-items: center;
      gap: 0.75rem;
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
    .danger-zone {
      border-color: var(--danger);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserDetailPage {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly minLength = PASSWORD_MIN_LENGTH;
  protected readonly capabilities = CAPABILITIES;
  protected readonly grantSettings = GRANT_SETTINGS;

  readonly userId = input.required<string>();
  protected readonly user = signal<AdminUser | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly renameText = signal('');
  protected readonly resetPassword = signal('');
  protected readonly modelVersion = signal(0);
  /** 擴充能力的覆寫草稿與全域預設（用來顯示「依全域預設」的結果）。 */
  protected readonly extensions = signal<{
    draft: Required<SaveUserExtensionsRequest>;
    defaults: ExtensionValues;
  } | null>(null);
  private readonly me = computed(() => this.auth.user());

  constructor() {
    // 以網址的 userId 載入；router 重用元件時也會重新載入。
    effect(() => this.load(this.userId()));
  }

  private load(userId: string): void {
    this.loading.set(true);
    this.user.set(null);
    this.extensions.set(null);
    this.error.set(null);
    this.message.set(null);
    forkJoin({
      user: this.api.adminGetUser(userId),
      state: this.api.adminGetUserExtensions(userId),
      policy: this.api.adminGetExtensionPolicy(),
    }).subscribe({
      next: ({ user, state, policy }) => {
        this.setUser(user);
        this.extensions.set({
          draft: {
            skills: state.skills,
            mcp: state.mcp,
            internet: state.internet,
            oneDrive: state.oneDrive,
          },
          defaults: policy.defaults,
        });
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.loading.set(false);
      },
    });
  }

  private setUser(user: AdminUser): void {
    this.user.set(user);
    this.renameText.set(user.displayName);
  }

  protected methodLabel(user: AdminUser): string {
    return describeAuthMethod(user.authMethod);
  }

  protected canToggle(user: AdminUser): boolean {
    return canToggleUser(user, this.me());
  }

  protected roleOptions(user: AdminUser): UserRole[] {
    return assignableRoles(user, this.me());
  }

  protected roleLabel(role: UserRole): string {
    return describeRole(role);
  }

  protected rename(user: AdminUser): void {
    this.run(this.api.adminRenameUser(user.id, this.renameText().trim()), (updated) => {
      this.setUser(updated);
      this.message.set(`已將「${user.displayName}」改名為「${updated.displayName}」。`);
    });
  }

  protected changeRole(user: AdminUser, event: Event): void {
    const select = event.target as HTMLSelectElement;
    const role = select.value as UserRole;
    if (role === user.role) return;
    const warning =
      role === 'Guest'
        ? '訪客不能使用成員訊息與群組聊天。'
        : role === 'Admin'
          ? '管理員可以管理所有使用者與系統設定。'
          : '';
    if (!confirm(`確定把「${user.displayName}」改成${describeRole(role)}？${warning}`)) {
      // 取消時把下拉選單還原成原本的角色。
      select.value = user.role;
      return;
    }
    this.run(
      this.api.adminChangeUserRole(user.id, role),
      (updated) => {
        this.setUser(updated);
        this.message.set(`${updated.displayName} 已改為${describeRole(updated.role)}。`);
      },
      () => (select.value = user.role),
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
      this.setUser(updated);
      this.message.set(`${updated.displayName} 已${enable ? '啟用' : '停用'}。`);
    });
  }

  protected reset(user: AdminUser): void {
    const problem = newPasswordProblem(this.resetPassword(), this.resetPassword());
    if (problem) {
      this.error.set(problem);
      return;
    }
    this.run(this.api.adminResetPassword(user.id, this.resetPassword()), () => {
      this.resetPassword.set('');
      this.message.set(`已重設 ${user.accountName} 的密碼；對方下次登入時必須變更。`);
    });
  }

  protected grantText(setting: ExtensionGrantSetting, defaultAllowed: boolean): string {
    return grantLabel(setting, defaultAllowed);
  }

  protected effective(setting: ExtensionGrantSetting, defaultAllowed: boolean): boolean {
    return effectiveValue(setting, defaultAllowed);
  }

  protected patchExtension(key: CapabilityKey, value: ExtensionGrantSetting): void {
    this.extensions.update((e) => (e ? { ...e, draft: { ...e.draft, [key]: value } } : e));
  }

  protected saveExtensions(user: AdminUser): void {
    const editing = this.extensions();
    if (!editing) return;
    this.run(this.api.adminSaveUserExtensions(user.id, editing.draft), () => {
      this.message.set(`已更新 ${user.displayName} 的擴充能力，從下一則訊息開始生效。`);
    });
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
      void this.router.navigate(['/admin/users']);
    });
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
