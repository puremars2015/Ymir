import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Observable, Subscription } from 'rxjs';
import { describeApiError } from '../../core/api/api.service';
import { PersonalMcpApi, PersonalMcpInfo } from './personal-mcp-api';

@Component({
  selector: 'app-personal-mcp-card',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="stack" aria-label="個人 MCP 工具">
      <h2>個人 MCP 工具</h2>
      <p class="muted">
        新增你信任的 HTTPS MCP 服務。新增／啟用後，Agent
        可從下次個人對話執行開始使用；不帶入群組共用環境。連線測試只執行 initialize，不呼叫工具。
      </p>
      <p class="muted">
        MCP 憑證加密保存且不回顯，執行時會送入你的個人 Agent
        程序環境；同一環境中的程式可能讀取它，請使用最小權限 token。平台 MCP 憑證與 OneDrive
        授權不屬於這項設定。
      </p>
      @if (loading()) {
        <p role="status">正在讀取個人 MCP…</p>
      } @else if (!allowed()) {
        <p class="muted">管理員尚未開放 MCP 或對外連線能力；已有項目仍可停用或移除。</p>
      }
      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }
      @if (message()) {
        <p role="status">{{ message() }}</p>
      }
      <ul>
        @for (server of servers(); track server.id) {
          <li class="stack">
            <strong>{{ server.name }}</strong>
            <code>{{ server.url }}</code>
            <span
              >{{ server.enabled ? '已啟用' : '已停用' }} ·
              {{ server.hasCredential ? '已儲存憑證（不回顯）' : '無 Bearer token' }}</span
            >
            <div class="actions">
              <button
                type="button"
                class="secondary"
                [disabled]="busy() || loading() || !allowed()"
                (click)="test(server)"
              >
                測試連線
              </button>
              <button
                type="button"
                class="secondary"
                [disabled]="busy() || loading() || (!allowed() && !server.enabled)"
                (click)="toggle(server)"
              >
                {{ server.enabled ? '停用' : '啟用' }}
              </button>
              <button
                type="button"
                class="secondary"
                [disabled]="busy() || loading()"
                (click)="remove(server)"
              >
                移除
              </button>
            </div>
          </li>
        } @empty {
          @if (!loading()) {
            <li class="muted">尚未註冊個人 MCP。</li>
          }
        }
      </ul>
      @if (allowed()) {
        <form class="stack" (ngSubmit)="add()" aria-label="新增個人 MCP">
          <h3>新增 MCP</h3>
          <label
            >名稱<input
              name="mcpName"
              maxlength="40"
              pattern="[a-z][a-z0-9-]{0,39}"
              required
              [ngModel]="name()"
              (ngModelChange)="name.set($event)"
              placeholder="例如：my-tools"
              [disabled]="busy()"
          /></label>
          <label
            >HTTPS MCP 網址<input
              name="mcpUrl"
              type="url"
              maxlength="2048"
              required
              [ngModel]="url()"
              (ngModelChange)="url.set($event)"
              placeholder="https://tools.example.com/mcp"
              [disabled]="busy()"
          /></label>
          <span class="muted"
            >僅 HTTPS 443，不接受帳密、查詢參數或
            fragment；測試拒絕內網／localhost，並不跟隨重新導向。</span
          >
          <label
            >Bearer token（選填）<input
              name="mcpToken"
              type="password"
              autocomplete="new-password"
              maxlength="4096"
              [ngModel]="token()"
              (ngModelChange)="token.set($event)"
              [disabled]="busy()"
          /></label>
          <button
            type="submit"
            [disabled]="
              busy() || loading() || !name().trim() || !url().trim() || servers().length >= 20
            "
          >
            新增並啟用 MCP
          </button>
        </form>
      }
      <button type="button" class="secondary" [disabled]="busy() || loading()" (click)="load()">
        重新整理連線清單
      </button>
    </section>
  `,
  styles: `
    h2,
    h3,
    p {
      margin: 0;
    }
    ul {
      list-style: none;
      padding: 0;
      margin: 0;
    }
    li {
      border: 1px solid var(--border);
      padding: 0.75rem;
      margin-bottom: 0.5rem;
      border-radius: 0.5rem;
      overflow-wrap: anywhere;
    }
    label {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
    form {
      border-top: 1px solid var(--border);
      padding-top: 1rem;
    }
  `,
})
export class PersonalMcpCard implements OnInit {
  private readonly api = inject(PersonalMcpApi);
  private readonly destroy = inject(DestroyRef);
  protected readonly servers = signal<PersonalMcpInfo[]>([]);
  protected readonly allowed = signal(false);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly name = signal('');
  protected readonly url = signal('');
  protected readonly token = signal('');
  private loadRequest: Subscription | null = null;

  constructor() {
    this.destroy.onDestroy(() => this.token.set(''));
  }
  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loadRequest?.unsubscribe();
    this.loading.set(true);
    this.loadRequest = this.api
      .list()
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (result) => {
          this.servers.set(result.servers);
          this.allowed.set(result.allowed);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.error.set(describeApiError(error));
          this.loading.set(false);
          this.allowed.set(false);
        },
      });
  }

  protected add(): void {
    if (this.busy() || !this.allowed()) return;
    const request = this.api.add(this.name().trim(), this.url().trim(), this.token() || null);
    this.token.set('');
    this.act(request, () => {
      this.name.set('');
      this.url.set('');
      this.message.set('MCP 已新增；從下次個人執行開始生效。');
      this.load();
    });
  }
  protected toggle(server: PersonalMcpInfo): void {
    this.act(this.api.setEnabled(server.id, !server.enabled), () => {
      this.message.set('設定已更新；從下次個人執行開始生效。');
      this.load();
    });
  }
  protected remove(server: PersonalMcpInfo): void {
    this.act(this.api.remove(server.id), () => {
      this.message.set('已移除註冊與儲存的憑證；不會刪除遠端服務或已取得的本機檔案。');
      this.load();
    });
  }
  protected test(server: PersonalMcpInfo): void {
    this.act(this.api.test(server.id), (result) => this.message.set(result.message));
  }
  private act<T>(request: Observable<T>, next: (value: T) => void): void {
    if (this.busy() || this.loading()) return;
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    request.pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: (value) => {
        this.busy.set(false);
        next(value);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(describeApiError(error));
      },
    });
  }
}
