import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { ModelOption } from '../api/api-types';
import { effortLabels, effectiveThinking } from './thinking-options';

const PREFERRED_KEY = 'ymir.preferredModel';
const DEPTH_KEY = 'ymir.preferredThinking';

/**
 * 可選用的模型與使用者上次選的模型。
 * 偏好只是個人便利，存在 localStorage；讀寫失敗（無痕模式等）時忽略即可。
 */
@Injectable({ providedIn: 'root' })
export class ModelStore {
  private readonly api = inject(ApiService);
  private loading = false;

  readonly models = signal<ModelOption[]>([]);
  readonly depth = signal<string | null>(readDepth());
  readonly preferred = signal<string | null>(readPreferred());

  constructor() {
    const refresh = () => this.load();
    globalThis.window?.addEventListener('focus', refresh);
    inject(DestroyRef).onDestroy(() => globalThis.window?.removeEventListener('focus', refresh));
  }

  load(): void {
    if (this.loading) {
      return;
    }
    this.loading = true;
    this.api.listModels().subscribe({
      next: (models) => {
        this.models.set(models);
        this.loading = false;
      },
      error: () => (this.loading = false),
    });
  }

  thinkingFor(modelId: string | null): string | null {
    return effectiveThinking(
      this.models().find((m) => m.id === modelId),
      this.depth(),
    );
  }

  rememberDepth(level: string | null): void {
    if (level !== null && !Object.hasOwn(effortLabels, level)) return;
    this.depth.set(level);
    try {
      localStorage.setItem(DEPTH_KEY, level ?? 'auto');
    } catch {
      /* 偏好保存失敗不影響送出 */
    }
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

function readDepth(): string | null {
  try {
    const level = localStorage.getItem(DEPTH_KEY);
    return level && Object.hasOwn(effortLabels, level) ? level : null;
  } catch {
    return null;
  }
}
