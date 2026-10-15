import { inject, Injectable, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { Tool } from '../api/api-types';

/**
 * 左側「小工具」（ADR-0029）：自己用 /make 小工具主題做出、平台自動發布的前端頁面。
 * 執行結束時重新讀取（後端在完成事件之前就已發布）。
 */
@Injectable({ providedIn: 'root' })
export class ToolStore {
  private readonly api = inject(ApiService);

  readonly tools = signal<Tool[]>([]);
  /** 網站託管已設定；未設定時小工具無法發布與開啟，側邊欄不顯示這個區塊。 */
  readonly enabled = signal(false);
  readonly loaded = signal(false);

  refresh(): void {
    this.api.listTools().subscribe({
      next: (result) => {
        this.enabled.set(result.enabled);
        this.tools.set(result.tools);
        this.loaded.set(true);
      },
      // 讀不到就維持原本的清單；側邊欄不因此顯示錯誤。
      error: () => this.loaded.set(true),
    });
  }

  find(id: string): Tool | undefined {
    return this.tools().find((t) => t.id === id);
  }
}
