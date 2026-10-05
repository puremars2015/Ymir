import { inject, Injectable, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { ModelOption } from '../api/api-types';

const PREFERRED_KEY = 'ymir.preferredModel';

/**
 * 可選用的模型與使用者上次選的模型。
 * 偏好只是個人便利，存在 localStorage；讀寫失敗（無痕模式等）時忽略即可。
 */
@Injectable({ providedIn: 'root' })
export class ModelStore {
  private readonly api = inject(ApiService);
  private loading = false;

  readonly models = signal<ModelOption[]>([]);
  readonly preferred = signal<string | null>(readPreferred());

  load(): void {
    if (this.loading || this.models().length) {
      return;
    }
    this.loading = true;
    this.api.listModels().subscribe({
      next: (models) => this.models.set(models),
      error: () => (this.loading = false),
    });
  }

  remember(modelId: string): void {
    this.preferred.set(modelId);
    try {
      localStorage.setItem(PREFERRED_KEY, modelId);
    } catch {
      // 無法保存偏好不影響功能
    }
  }
}

function readPreferred(): string | null {
  try {
    return localStorage.getItem(PREFERRED_KEY);
  } catch {
    return null;
  }
}
