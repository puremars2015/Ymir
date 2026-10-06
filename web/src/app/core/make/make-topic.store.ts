import { inject, Injectable, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { MakeTopic } from '../api/api-types';

/** `/make` 的主題按鈕。每次開啟主題選單都重新讀取，Admin 修改後不需要重新整理頁面。 */
@Injectable({ providedIn: 'root' })
export class MakeTopicStore {
  private readonly api = inject(ApiService);

  readonly topics = signal<MakeTopic[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  refresh(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.listMakeTopics().subscribe({
      next: (topics) => {
        this.topics.set(topics);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('無法載入主題，請稍後再試。');
        this.loading.set(false);
      },
    });
  }
}
