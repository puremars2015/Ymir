import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import {
  MCP_ACCESS_MODES,
  mcpAccessSummary,
  toggleUser,
} from '../../core/admin/platform-mcp-rules';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { AdminUser, McpAccessMode, McpServerAccess } from '../../core/api/api-types';

/**
 * 系統設定：平台 MCP 服務（ADR-0012 B）。服務由開發人員在版控的目錄（deploy/mcp/servers.json）定義，
 * 這裡只能啟用 / 停用與設定可用對象；變更在成員的下一次執行生效（最慢在 gateway token 期限內）。
 */
@Component({
  selector: 'app-platform-mcp-card',
  imports: [FormsModule],
  template: `
    <article class="card stack">
      <header>
        <h2>平台 MCP 服務</h2>
        <p class="muted small">
          由開發人員上線的平台服務（例如公司內部系統）。Agent 只能經 MCP
          Gateway、以每人專屬的短期授權使用， 拿不到任何後端憑證。
        </p>
      </header>

      @if (loading()) {
        <p class="muted">載入中…</p>
      } @else if (!enabled()) {
        <p class="muted">尚未設定 MCP Gateway（Ymir:Mcp:GatewayUrl），平台 MCP 停用。</p>
      } @else if (servers().length === 0) {
        <p class="muted">服務目錄中沒有服務。</p>
      } @else {
        @for (server of servers(); track server.name) {
          <section class="server stack" [attr.data-server]="server.name">
            <div class="row between">
              <div>
                <strong>{{ server.name }}</strong>
                <span class="muted small block">{{ server.description }}</span>
              </div>
              <span class="muted small">{{ summary(server) }}</span>
            </div>
            <label class="check">
              <input
                type="checkbox"
                [name]="server.name + '-enabled'"
                [ngModel]="server.enabled"
                (ngModelChange)="patch(server.name, { enabled: $event })"
              />
              <span>啟用</span>
            </label>
            <label class="grow"
              >可用對象
              <select
                [name]="server.name + '-mode'"
                [ngModel]="server.mode"
                (ngModelChange)="patch(server.name, { mode: $event })"
              >
                @for (m of modes; track m.value) {
                  <option [value]="m.value">{{ m.label }}</option>
                }
              </select>
            </label>
            @if (server.mode === 'SelectedUsers') {
              <div class="users">
                @for (user of users(); track user.id) {
                  <label class="check">
                    <input
                      type="checkbox"
                      [checked]="server.userIds.includes(user.id)"
                      (change)="toggle(server.name, user.id, $any($event.target).checked)"
                    />
                    <span
                      >{{ user.displayName }}
                      <span class="muted small">{{ user.accountName ?? user.email }}</span></span
                    >
                  </label>
                }
              </div>
            }
            <div class="row">
              <button type="button" [disabled]="busy()" (click)="save(server)">儲存</button>
            </div>
          </section>
        }
      }

      @if (message()) {
        <p class="muted" role="status">{{ message() }}</p>
      }
      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }
    </article>
  `,
  styles: `
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
    .block {
      display: block;
    }
    .server {
      padding: 0.75rem;
      border: 1px solid var(--border);
      border-radius: 0.5rem;
    }
    .row {
      display: flex;
      gap: 0.75rem;
      align-items: center;
    }
    .between {
      justify-content: space-between;
    }
    .check {
      display: flex;
      flex-direction: row;
      gap: 0.5rem;
      align-items: flex-start;
    }
    .grow {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    .users {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      max-height: 12rem;
      overflow-y: auto;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlatformMcpCard implements OnInit {
  private readonly api = inject(ApiService);

  protected readonly modes = MCP_ACCESS_MODES;
  protected readonly loading = signal(true);
  protected readonly enabled = signal(false);
  protected readonly servers = signal<McpServerAccess[]>([]);
  protected readonly users = signal<AdminUser[]>([]);
  protected readonly busy = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    forkJoin([this.api.adminListMcpServers(), this.api.adminListUsers('')]).subscribe({
      next: ([servers, users]) => {
        this.enabled.set(servers.enabled);
        this.servers.set(servers.servers);
        this.users.set(users.filter((u) => u.status === 'Active'));
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.loading.set(false);
      },
    });
  }

  protected summary(server: McpServerAccess): string {
    return mcpAccessSummary(server);
  }

  protected patch(name: string, change: { enabled?: boolean; mode?: McpAccessMode }): void {
    this.servers.update((list) => list.map((s) => (s.name === name ? { ...s, ...change } : s)));
  }

  protected toggle(name: string, userId: string, selected: boolean): void {
    this.servers.update((list) =>
      list.map((s) =>
        s.name === name ? { ...s, userIds: toggleUser(s.userIds, userId, selected) } : s,
      ),
    );
  }

  protected save(server: McpServerAccess): void {
    this.busy.set(true);
    this.message.set(null);
    this.error.set(null);
    this.api
      .adminSaveMcpServerAccess(server.name, {
        enabled: server.enabled,
        mode: server.mode,
        userIds: server.mode === 'SelectedUsers' ? server.userIds : [],
      })
      .subscribe({
        next: (saved) => {
          this.servers.update((list) => list.map((s) => (s.name === saved.name ? saved : s)));
          this.message.set(
            `已儲存 ${saved.name}：${mcpAccessSummary(saved)}。成員的下一次執行開始生效。`,
          );
          this.busy.set(false);
        },
        error: (e: unknown) => {
          this.error.set(describeApiError(e));
          this.busy.set(false);
        },
      });
  }
}
