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
import { ApiService, describeApiError } from '../../core/api/api.service';
import { Branding } from '../../core/api/api-types';
import {
  companyNameProblem,
  faviconProblem,
  MAX_COMPANY_NAME_LENGTH,
  PROJECT_NAME,
} from '../../core/branding/branding-rules';
import { BrandingStore } from '../../core/branding/branding.store';

/**
 * 系統設定：公司識別。Ymir 是開源專案名稱、不可修改；管理者輸入自己的公司名稱（顯示為「公司名稱 Ymir」）
 * 並可上傳 favicon（PNG / ICO / JPEG / WebP，256 KB 內）。
 */
@Component({
  selector: 'app-branding-settings-card',
  imports: [FormsModule],
  template: `
    <article class="card stack">
      <header>
        <h2>公司識別</h2>
        <p class="muted small">
          平台名稱固定為 {{ projectName }}，這裡設定要顯示在它前面的公司名稱與網站圖示（favicon）。
        </p>
      </header>

      <form class="stack" (ngSubmit)="saveName()">
        <label
          >公司名稱
          <input
            name="companyName"
            autocomplete="off"
            placeholder="例如：Acme（留空則只顯示 Ymir）"
            [attr.maxlength]="maxLength"
            [ngModel]="companyName()"
            (ngModelChange)="companyName.set($event)"
          />
        </label>
        <p class="muted small">
          預覽：<strong>{{ preview() }}</strong>
        </p>
        <div class="row">
          <button type="submit" [disabled]="busy() || !!nameProblem()">儲存名稱</button>
          @if (nameProblem(); as p) {
            <span class="muted small">{{ p }}</span>
          }
        </div>
      </form>

      <div class="stack">
        <span>Favicon</span>
        <div class="row">
          @if (faviconUrl(); as url) {
            <img class="icon-preview" [src]="url" alt="目前的 favicon" />
          } @else {
            <span class="muted small">使用預設圖示</span>
          }
        </div>
        <div class="row">
          <input
            #file
            type="file"
            accept="image/png,image/x-icon,image/vnd.microsoft.icon,image/jpeg,image/webp,.ico"
            [disabled]="busy()"
            (change)="upload(file)"
          />
          @if (faviconUrl()) {
            <button type="button" class="secondary" [disabled]="busy()" (click)="removeFavicon()">
              還原預設圖示
            </button>
          }
        </div>
        <p class="muted small">
          建議使用正方形圖片（例如 64×64 PNG）。瀏覽器可能需要重新整理才會更新分頁圖示。
        </p>
      </div>

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
    .icon-preview {
      width: 2rem;
      height: 2rem;
      object-fit: contain;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BrandingSettingsCard implements OnInit {
  private readonly api = inject(ApiService);
  private readonly store = inject(BrandingStore);

  protected readonly projectName = PROJECT_NAME;
  protected readonly maxLength = MAX_COMPANY_NAME_LENGTH;
  protected readonly companyName = signal('');
  protected readonly faviconUrl = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);

  protected readonly nameProblem = computed(() => companyNameProblem(this.companyName()));
  protected readonly preview = computed(() => {
    const company = this.companyName().trim();
    return company ? `${company} ${PROJECT_NAME}` : PROJECT_NAME;
  });

  ngOnInit(): void {
    this.api.getBranding().subscribe({
      next: (b) => this.apply(b),
      error: (e: unknown) => this.error.set(describeApiError(e)),
    });
  }

  protected saveName(): void {
    if (this.nameProblem()) return;
    this.run(this.api.adminSaveBranding(this.companyName().trim()), '已儲存公司名稱。');
  }

  protected async upload(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    const header = new Uint8Array(await file.slice(0, 12).arrayBuffer());
    const problem = faviconProblem(file.size, header);
    if (problem) {
      this.message.set(null);
      this.error.set(problem);
      return;
    }
    this.run(this.api.adminUploadFavicon(file), '已更新 favicon。');
  }

  protected removeFavicon(): void {
    this.run(this.api.adminDeleteFavicon(), '已還原預設圖示。');
  }

  private apply(branding: Branding): void {
    this.companyName.set(branding.companyName);
    this.faviconUrl.set(branding.faviconUrl ?? null);
    this.store.set(branding);
  }

  private run(request: Observable<Branding>, done: string): void {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    request.subscribe({
      next: (b) => {
        this.apply(b);
        this.message.set(done);
        this.busy.set(false);
      },
      error: (e: unknown) => {
        this.error.set(describeApiError(e));
        this.busy.set(false);
      },
    });
  }
}
