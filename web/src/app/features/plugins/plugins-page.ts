import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { Plugin } from '../../core/api/api-types';
import {
  enableConfirmation,
  pluginStatusLabel,
  replacePlugin,
  sortPlugins,
} from '../../core/plugins/plugin-rules';
import { MyExtensionsCard } from '../settings/my-extensions-card';
import { OneDriveCard } from '../settings/onedrive-card';
import { PersonalMcpCard } from '../settings/personal-mcp-card';

/**
 * 外掛市集（ADR-0030）：管理員決定誰能用，使用者在這裡安裝（連結帳號）並決定要不要讓 Agent 使用。
 * OneDrive 的帳號連結與同步資料夾沿用 ADR-0013 的卡片；自訂外掛是個人 MCP（ADR-0028）。
 */
@Component({
  selector: 'app-plugins-page',
  imports: [OneDriveCard, PersonalMcpCard, MyExtensionsCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="market">
      <h1>外掛市集</h1>
      <p class="muted">
        外掛讓 Agent 在對話中使用其他服務。管理員決定哪些外掛可以用，你決定要不要讓 Agent 使用；
        變更從下一則訊息開始生效。
      </p>

      @if (error(); as e) {
        <p class="error" role="alert">{{ e }}</p>
      }

      <ul class="cards" aria-label="外掛">
        @for (plugin of plugins(); track plugin.key) {
          <li class="card">
            <header>
              <span class="icon" aria-hidden="true">{{
                plugin.kind === 'OneDrive' ? '☁' : '⚙'
              }}</span>
              <div class="title">
                <h2>{{ plugin.name }}</h2>
                <span class="kind muted">{{
                  plugin.kind === 'OneDrive' ? '平台內建' : '平台服務'
                }}</span>
              </div>
              <span class="status" [class]="'status ' + status(plugin).tone">{{
                status(plugin).text
              }}</span>
            </header>
            <p>{{ plugin.description || '（沒有說明）' }}</p>
            <!-- 未開放 / 未設定登入的說明由下方 OneDrive 卡片顯示，這裡不重複。 -->
            @if (plugin.notice && (plugin.allowed || plugin.kind !== 'OneDrive')) {
              <p class="notice">{{ plugin.notice }}</p>
            }
            @if (plugin.kind === 'OneDrive') {
              <p class="muted hint">
                Agent 拿不到你的 Microsoft 授權；每次操作都由 Ymir
                代為執行並記錄。刪除的檔案會先放進 OneDrive 回收筒。
              </p>
            }
            <label class="toggle">
              <input
                type="checkbox"
                [checked]="plugin.enabled"
                [disabled]="busy() === plugin.key || (!plugin.enabled && !plugin.allowed)"
                (change)="toggle(plugin, $any($event.target).checked, $event)"
              />
              讓 Agent 使用
            </label>
            @if (plugin.kind === 'OneDrive') {
              <section class="account" aria-label="OneDrive 帳號">
                <h3>
                  {{
                    plugin.installed
                      ? '帳號與同步資料夾'
                      : plugin.allowed
                        ? '安裝：連結 OneDrive 帳號'
                        : 'OneDrive 帳號'
                  }}
                </h3>
                <app-onedrive-card (changed)="reload()" />
              </section>
            }
          </li>
        } @empty {
          @if (!loading()) {
            <li class="muted">目前沒有可用的外掛。</li>
          }
        }
      </ul>

      <h2 class="section">自訂外掛</h2>
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
    .market {
      max-width: 52rem;
      margin: 0 auto;
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }
    h1,
    h2,
    p {
      margin: 0;
    }
    .cards {
      list-style: none;
      padding: 0;
      margin: 0;
      display: grid;
      gap: 1rem;
    }
    .card {
      border: 1px solid var(--border);
      border-radius: 0.75rem;
      padding: 1rem 1.25rem;
      display: flex;
      flex-direction: column;
      gap: 0.625rem;
      background: var(--surface, transparent);
    }
    header {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      flex-wrap: wrap;
    }
    .icon {
      font-size: 1.5rem;
      width: 2.5rem;
      height: 2.5rem;
      display: grid;
      place-items: center;
      border-radius: 0.5rem;
      background: var(--accent-soft, rgba(127, 127, 127, 0.12));
    }
    .title {
      flex: 1;
      min-width: 8rem;
    }
    .title h2 {
      font-size: 1.0625rem;
    }
    .kind {
      font-size: 0.8125rem;
    }
    .status {
      font-size: 0.8125rem;
      padding: 0.125rem 0.625rem;
      border-radius: 999px;
      border: 1px solid var(--border);
      white-space: nowrap;
    }
    .status.ok {
      color: var(--success, #1a7f37);
      border-color: currentColor;
    }
    .status.warn {
      color: var(--warning, #9a6700);
      border-color: currentColor;
    }
    .status.muted {
      color: var(--text-muted);
    }
    .notice {
      color: var(--warning, #9a6700);
      font-size: 0.875rem;
    }
    .hint {
      font-size: 0.8125rem;
    }
    .toggle {
      display: inline-flex;
      align-items: center;
      gap: 0.5rem;
      font-weight: 600;
      width: fit-content;
    }
    .account {
      border-top: 1px solid var(--border);
      padding-top: 0.75rem;
    }
    .account h3 {
      margin: 0;
      font-size: 0.9375rem;
      color: var(--text-muted);
    }
    .section {
      margin-top: 1rem;
      font-size: 1.125rem;
    }
  `,
})
export class PluginsPage implements OnInit {
  private readonly api = inject(ApiService);

  protected readonly plugins = signal<Plugin[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.reload();
  }

  protected status(plugin: Plugin) {
    return pluginStatusLabel(plugin);
  }

  reload(): void {
    this.api.listPlugins().subscribe({
      next: (plugins) => {
        this.plugins.set(sortPlugins(plugins));
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.loading.set(false);
      },
    });
  }

  protected toggle(plugin: Plugin, enabled: boolean, event: Event): void {
    const question = enabled ? enableConfirmation(plugin) : null;
    if (question && !confirm(question)) {
      (event.target as HTMLInputElement).checked = false;
      return;
    }

    this.busy.set(plugin.key);
    this.error.set(null);
    this.api.setPluginEnabled(plugin.key, enabled).subscribe({
      next: (updated) => {
        this.plugins.update((list) => replacePlugin(list, updated));
        this.busy.set(null);
      },
      error: (e: unknown) => {
        (event.target as HTMLInputElement).checked = plugin.enabled;
        this.error.set(describeApiError(e));
        this.busy.set(null);
      },
    });
  }
}
