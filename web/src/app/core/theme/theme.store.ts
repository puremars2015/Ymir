import { DOCUMENT, inject, Injectable, signal } from '@angular/core';

export type ThemeMode = 'light' | 'dark' | 'auto';
const STORAGE_KEY = 'ymir.theme';

/** 主題偏好屬於這個瀏覽器；自動模式由 CSS 持續跟隨系統設定。 */
@Injectable({ providedIn: 'root' })
export class ThemeStore {
  private readonly document = inject(DOCUMENT);
  private readonly selected = signal<ThemeMode>('auto');
  readonly mode = this.selected.asReadonly();

  constructor() {
    let saved: string | null = null;
    try {
      saved = this.document.defaultView?.localStorage.getItem(STORAGE_KEY) ?? null;
    } catch {
      // 瀏覽器禁止儲存時仍能切換主題。
    }
    this.apply(saved === 'light' || saved === 'dark' ? saved : 'auto');
  }

  select(mode: ThemeMode): void {
    this.apply(mode);
    try {
      this.document.defaultView?.localStorage.setItem(STORAGE_KEY, mode);
    } catch {
      // 偏好無法保存不影響目前畫面。
    }
  }

  private apply(mode: ThemeMode): void {
    this.selected.set(mode);
    this.document.documentElement.dataset['theme'] = mode;
  }
}
