import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, OnInit, output, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Observable } from 'rxjs';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { OneDriveStatus } from '../../core/api/api-types';
import {
  ONEDRIVE_CONNECT_URL,
  onedriveResultMessage,
  onedriveStateLabel,
} from '../../core/connectors/onedrive-rules';

/**
 * 外掛市集的 OneDrive 帳號（ADR-0013、ADR-0030）：管理員開放後，使用者自己連結（= 安裝外掛），
 * 開啟「讓 Agent 使用」後 Agent 才能讀寫自己的 OneDrive；不同步工作目錄或對話紀錄（ADR-0033）。
 * 連結是整頁導向 Microsoft（授權碼 + PKCE 由後端處理），前端不接觸任何 token。
 */
@Component({
  selector: 'app-onedrive-card',
  imports: [DatePipe],
  template: `
    @if (status(); as s) {
      <section class="stack onedrive">
        <p>
          <strong>{{ stateLabel(s) }}</strong>
          @if (s.account) {
            <span class="muted">（{{ s.account }}）</span>
          }
        </p>
        @if (s.connectedAt) {
          <p class="muted hint">連結時間：{{ s.connectedAt | date: 'yyyy-MM-dd HH:mm' }}</p>
        }
        <p class="muted hint">
          連結後開啟上方「讓 Agent 使用」，Agent 才能在對話中搜尋、下載、上傳與整理你的
          OneDrive。對話與工作目錄不會自動同步到 OneDrive。
        </p>

        @if (!s.available) {
          <p class="error">企業帳號登入尚未設定，暫時無法連結 OneDrive。</p>
        } @else if (!s.allowed) {
          <p class="muted">
            管理員尚未開放 OneDrive，請洽管理員；若已有連結，Agent 存取已停止，仍可解除連結。
          </p>
        }

        <div class="row">
          @if (s.allowed && s.available) {
            <button type="button" [disabled]="busy()" (click)="connect()">
              {{ s.state === 'NotConnected' ? '連結 OneDrive' : '重新連結' }}
            </button>
          }
          @if (s.state !== 'NotConnected') {
            <button type="button" class="secondary" [disabled]="busy()" (click)="disconnect()">
              解除連結
            </button>
          }
        </div>

        @if (message(); as m) {
          <p [class.error]="m.error" [class.muted]="!m.error" role="status">{{ m.text }}</p>
        }
      </section>
    } @else {
      <p role="status">{{ message()?.text ?? '正在讀取 OneDrive 連線狀態…' }}</p>
    }
  `,
  styles: `
    .onedrive {
      margin-top: 0.75rem;
    }
    p {
      margin: 0;
    }
    .hint {
      font-size: 0.8125rem;
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      gap: 0.75rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OneDriveCard implements OnInit {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly status = signal<OneDriveStatus | null>(null);
  protected readonly busy = signal(false);
  protected readonly message = signal<{ text: string; error: boolean } | null>(null);
  /** 連結狀態改變（連結結果、解除連結）：外掛市集據此重新讀取卡片。 */
  readonly changed = output<void>();

  ngOnInit(): void {
    // 從 Microsoft 導回時帶 ?onedrive=結果；顯示後從網址移除，重新整理不會再出現。
    const result = onedriveResultMessage(this.route.snapshot.queryParamMap.get('onedrive'));
    if (result) {
      this.message.set(result);
      void this.router.navigate([], {
        queryParams: { onedrive: null },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    }

    this.api.getOneDriveStatus().subscribe({
      next: (s) => this.apply(s),
      error: (e: unknown) => this.message.set({ text: describeApiError(e), error: true }),
    });
  }

  protected stateLabel(status: OneDriveStatus): string {
    return onedriveStateLabel(status);
  }

  protected connect(): void {
    window.location.assign(ONEDRIVE_CONNECT_URL);
  }

  protected disconnect(): void {
    if (
      !confirm(
        '解除 OneDrive 連結？雲端與執行環境內的檔案都會保留，Agent 之後不能再使用 OneDrive。',
      )
    ) {
      return;
    }

    this.run(this.api.disconnectOneDrive(), '已解除連結。');
  }

  private run(request: Observable<OneDriveStatus>, done: string): void {
    this.busy.set(true);
    this.message.set(null);
    request.subscribe({
      next: (s) => {
        this.apply(s);
        this.message.set({ text: done, error: false });
        this.busy.set(false);
        this.changed.emit();
      },
      error: (e: unknown) => {
        this.message.set({ text: describeApiError(e), error: true });
        this.busy.set(false);
      },
    });
  }

  private apply(status: OneDriveStatus): void {
    this.status.set(status);
  }
}
