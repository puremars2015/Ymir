import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
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
 * 個人設定：OneDrive 連結（ADR-0013）。管理員開放後，使用者自己連結並選擇要同步的資料夾。
 * 連結是整頁導向 Microsoft（授權碼 + PKCE 由後端處理），前端不接觸任何 token。
 */
@Component({
  selector: 'app-onedrive-card',
  imports: [DatePipe, FormsModule],
  template: `
    @if (status(); as s) {
      @if (s.allowed || s.state !== 'NotConnected') {
        <section class="stack onedrive">
          <h2>OneDrive</h2>
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
            執行 Agent
            前會從這個資料夾下載檔案，執行後把新增或修改的檔案上傳；兩邊同時修改時會保留兩份。Agent
            本身拿不到你的 OneDrive 授權。
          </p>

          @if (!s.available) {
            <p class="error">企業帳號登入尚未設定，暫時無法連結 OneDrive。</p>
          } @else if (!s.allowed) {
            <p class="muted">管理員已關閉 OneDrive 連結，同步已停止；你可以解除連結。</p>
          }

          @if (s.state === 'Connected' && s.allowed) {
            <form class="row" (ngSubmit)="saveRoot()">
              <label class="grow"
                >同步資料夾
                <input
                  name="rootPath"
                  [ngModel]="rootPath()"
                  (ngModelChange)="rootPath.set($event)"
                  placeholder="/Ymir"
                />
              </label>
              <button type="submit" [disabled]="busy() || !rootPath().trim()">儲存</button>
            </form>
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
      }
    }
  `,
  styles: `
    .onedrive {
      margin-top: 2rem;
    }
    h2 {
      margin: 0;
      font-size: 1.125rem;
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
    .grow {
      flex: 1;
      min-width: 12rem;
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OneDriveCard implements OnInit {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly status = signal<OneDriveStatus | null>(null);
  protected readonly rootPath = signal('/Ymir');
  protected readonly busy = signal(false);
  protected readonly message = signal<{ text: string; error: boolean } | null>(null);

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

  protected saveRoot(): void {
    this.run(this.api.setOneDriveRoot(this.rootPath().trim()), '已設定同步資料夾。');
  }

  protected disconnect(): void {
    if (!confirm('解除 OneDrive 連結？雲端與執行環境內的檔案都會保留，之後不再同步。')) {
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
      },
      error: (e: unknown) => {
        this.message.set({ text: describeApiError(e), error: true });
        this.busy.set(false);
      },
    });
  }

  private apply(status: OneDriveStatus): void {
    this.status.set(status);
    if (status.rootPath) {
      this.rootPath.set(status.rootPath);
    }
  }
}
