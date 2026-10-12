import { DOCUMENT } from '@angular/common';
import { computed, inject, Injectable, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { Branding } from '../api/api-types';
import { brandName } from './branding-rules';

/**
 * 公司識別（公司名稱 + favicon），由管理者在系統設定輸入。登入前就要顯示，所以後端讀取端點是匿名的；
 * 取不到時沿用預設（只顯示 Ymir），不影響使用。
 */
@Injectable({ providedIn: 'root' })
export class BrandingStore {
  private readonly api = inject(ApiService);
  private readonly document = inject(DOCUMENT);
  private readonly current = signal<Branding | null>(null);

  readonly companyName = computed(() => this.current()?.companyName ?? '');
  readonly name = computed(() => brandName(this.companyName()));

  constructor() {
    this.api.getBranding().subscribe({ next: (b) => this.set(b), error: () => undefined });
  }

  /** 管理者存檔後直接套用，不必重新整理。 */
  set(branding: Branding): void {
    this.current.set(branding);
    if (branding.faviconUrl) {
      const head = this.document.head;
      let link = head.querySelector<HTMLLinkElement>('link[rel="icon"]');
      if (!link) {
        link = this.document.createElement('link');
        link.rel = 'icon';
        head.appendChild(link);
      }
      link.removeAttribute('type');
      link.href = branding.faviconUrl;
    }
  }
}
