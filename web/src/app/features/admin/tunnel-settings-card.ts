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
import { Observable } from 'rxjs';
import { oidcSourceLabel } from '../../core/admin/oidc-settings-rules';
import {
  extractTunnelToken,
  hostnameProblem,
  tunnelState,
  tunnelStateLabel,
  tunnelTokenProblem,
} from '../../core/admin/tunnel-settings-rules';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { TunnelSettings } from '../../core/api/api-types';

/**
 * 系統設定：Cloudflare Tunnel（ADR-0010）。token 只能寫入，經 API 轉給主機上的 runtime host 套用，
 * API 與資料庫都不保存；對外網域存檔後立即生效（Host 限制）。
 */
@Component({
  selector: 'app-tunnel-settings-card',
  imports: [DatePipe, FormsModule],
  template: `
    <article class="card stack">
      <header>
        <h2>對外連線（Cloudflare Tunnel）</h2>
        @if (settings(); as s) {
          <p class="muted small">
            狀態：<span class="state" [attr.data-state]="state()">● {{ stateLabel() }}</span>
            @if (s.tokenUpdatedAt) {
              · token 更新於 {{ s.tokenUpdatedAt | date: 'yyyy-MM-dd HH:mm' }}
            }
            @if (!s.publicEdgeEnabled) {
              · 部署設定尚未開啟對外公開（Ymir__PublicEdge__Enabled）
            }
          </p>
        }
      </header>

      @if (settings(); as s) {
        <form class="stack" (ngSubmit)="saveToken()">
          <label
            >Tunnel token
            <input
              name="tunnelToken"
              type="password"
              autocomplete="off"
              spellcheck="false"
              [placeholder]="s.configured ? '已設定（輸入新的 token 會取代）' : '尚未設定'"
              [disabled]="!s.managementAvailable"
              [ngModel]="token()"
              (ngModelChange)="token.set($event)"
            />
            <span class="muted small"
              >Cloudflare dashboard → Zero Trust → Networks → Tunnels → 你的 tunnel → 安裝
              connector， 可以直接貼上整行指令，系統會取出 token。</span
            >
          </label>
          <div class="row">
            <button type="submit" [disabled]="busy() || !s.managementAvailable || !!tokenProblem()">
              套用 token
            </button>
            @if (token() && tokenProblem(); as p) {
              <span class="muted small">{{ p }}</span>
            }
          </div>
          @if (!s.managementAvailable) {
            <p class="muted small">
              這個部署沒有經由主機上的 runtime host 管理 tunnel（例如 Windows 開發機）。請依
              docs/guides/cloudflare-tunnel.md 在主機上設定 token。
            </p>
          }
        </form>

        <form class="stack" (ngSubmit)="saveHostname()">
          <label
            >對外網域
            <input
              name="hostname"
              spellcheck="false"
              placeholder="ymir.example.com"
              [ngModel]="hostname()"
              (ngModelChange)="hostname.set($event)"
            />
            <span class="muted small"
              >目前來源：{{
                hostnameSource()
              }}。只接受這個網域與本機的連線；留空存檔表示還原為部署設定。</span
            >
          </label>
          <div class="row">
            <button type="submit" class="secondary" [disabled]="busy() || !!hostnameError()">
              儲存網域
            </button>
            @if (hostnameError(); as p) {
              <span class="muted small">{{ p }}</span>
            }
          </div>
          @if (s.redirectUri) {
            <p class="note small">
              變更網域後，請同步：① Cloudflare dashboard 的 Public Hostname 指向
              <code>http://127.0.0.1:5080</code>；② Entra 的重新導向 URI 加入
              <code>{{ s.redirectUri }}</code
              >。
            </p>
          }
        </form>
      } @else if (loading()) {
        <p class="muted">載入中…</p>
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
    label {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.75rem;
    }
    .state {
      font-weight: 600;
      color: var(--text-muted);
    }
    .state[data-state='connected'] {
      color: #15803d;
    }
    .state[data-state='disconnected'] {
      color: var(--danger);
    }
    .note {
      padding: 0.625rem 0.75rem;
      border-radius: 0.5rem;
      background: var(--surface-muted);
    }
    code {
      overflow-wrap: anywhere;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TunnelSettingsCard implements OnInit {
  private readonly api = inject(ApiService);

  protected readonly settings = signal<TunnelSettings | null>(null);
  protected readonly token = signal('');
  protected readonly hostname = signal('');
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);

  protected readonly state = computed(() => {
    const s = this.settings();
    return s ? tunnelState(s) : 'unavailable';
  });
  protected readonly stateLabel = computed(() => tunnelStateLabel(this.state()));
  protected readonly tokenProblem = computed(() =>
    tunnelTokenProblem(extractTunnelToken(this.token())),
  );
  protected readonly hostnameError = computed(() => hostnameProblem(this.hostname()));
  protected readonly hostnameSource = computed(() =>
    oidcSourceLabel(this.settings()?.hostnameSource ?? 'None'),
  );

  ngOnInit(): void {
    this.api.adminGetTunnelSettings().subscribe({
      next: (settings) => {
        this.apply(settings);
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.loading.set(false);
      },
    });
  }

  protected saveToken(): void {
    if (this.tokenProblem()) return;
    this.run(this.api.adminSetTunnelToken(extractTunnelToken(this.token())), (settings) => {
      this.apply(settings);
      this.message.set(
        settings.active ? '已套用 token，Cloudflare Tunnel 已重新連線。' : '已套用 token。',
      );
    });
  }

  protected saveHostname(): void {
    if (this.hostnameError()) return;
    this.run(this.api.adminSetPublicHostname(this.hostname().trim()), (settings) => {
      this.apply(settings);
      this.message.set(
        settings.hostname ? `對外網域已改為 ${settings.hostname}。` : '已還原為部署設定。',
      );
    });
  }

  private apply(settings: TunnelSettings): void {
    this.settings.set(settings);
    this.token.set('');
    this.hostname.set(settings.hostname ?? '');
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
