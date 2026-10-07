import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { concatMap, from, Subscription, timer } from 'rxjs';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { KnowledgeBase, KnowledgeDocument } from '../../core/api/api-types';
import { formatSize } from '../../core/files/workspace-files';
import {
  hasPendingDocuments,
  knowledgeStatusLabel,
  validateKnowledgeFile,
} from '../../core/knowledge/knowledge-rules';

/**
 * 專案知識庫（ADR-0014）：上傳文件後由伺服器在背景建立索引；只有專案擁有者看得到。
 * 文件保存在平台的知識庫儲存區，不在 Agent 的工作目錄。
 */
@Component({
  selector: 'app-knowledge-card',
  imports: [DatePipe],
  template: `
    <section class="knowledge stack" aria-label="知識庫">
      <h2>知識庫</h2>
      @if (kb(); as k) {
        @if (!k.enabled) {
          <p class="muted">知識庫尚未設定（需要 Embedding 模型），請洽管理員。</p>
        } @else {
          <p class="muted small">
            上傳文件後會建立索引，之後可以根據文件提問並看到出處。支援
            {{ k.supportedExtensions.join('、') }}，單檔 {{ maxMb(k) }} MB 以內；掃描檔需要
            OCR，目前不支援。
          </p>
          <label class="upload">
            <input
              type="file"
              multiple
              [accept]="k.supportedExtensions.join(',')"
              [disabled]="busy()"
              (change)="upload($any($event.target))"
            />
          </label>
          <ul class="documents">
            @for (d of k.documents; track d.id) {
              <li [attr.data-status]="d.status">
                <span class="name" [title]="d.fileName">{{ d.fileName }}</span>
                <span class="muted small">
                  {{ status(d) }}
                  @if (d.status === 'Ready') {
                    · {{ d.chunkCount }} 段
                  }
                  · {{ size(d) }} · v{{ d.version }} · {{ d.updatedAt | date: 'M/d HH:mm' }}
                </span>
                @if (d.error) {
                  <span class="error small">{{ d.error }}</span>
                }
                <span class="actions">
                  @if (d.status === 'Failed') {
                    <button type="button" class="link small" (click)="retry(d)">重試</button>
                  }
                  <button type="button" class="link small" (click)="remove(d)">移除</button>
                </span>
              </li>
            } @empty {
              <li class="muted">還沒有文件。</li>
            }
          </ul>
        }
      } @else if (!error()) {
        <p class="muted">載入中…</p>
      }
      @if (error(); as e) {
        <p class="error" role="alert">{{ e }}</p>
      }
    </section>
  `,
  styles: `
    .knowledge {
      margin-top: 1.5rem;
    }
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
    .documents {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 0.375rem;
    }
    .documents li {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      gap: 0.5rem;
      padding: 0.5rem 0.75rem;
      border: 1px solid var(--border);
      border-radius: 0.5rem;
    }
    .name {
      font-weight: 600;
      overflow: hidden;
      text-overflow: ellipsis;
      max-width: 100%;
    }
    .actions {
      margin-left: auto;
      display: flex;
      gap: 0.75rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KnowledgeCard {
  private readonly api = inject(ApiService);

  readonly projectId = input.required<string>();
  protected readonly kb = signal<KnowledgeBase | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  private poll: Subscription | null = null;

  constructor() {
    effect(() => {
      this.projectId();
      untracked(() => {
        this.kb.set(null);
        this.error.set(null);
        this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => this.poll?.unsubscribe());
  }

  protected status(document: KnowledgeDocument): string {
    return knowledgeStatusLabel(document.status);
  }

  protected size(document: KnowledgeDocument): string {
    return formatSize(document.size);
  }

  protected maxMb(kb: KnowledgeBase): number {
    return Math.floor(Number(kb.maxFileBytes) / 1024 / 1024);
  }

  protected upload(input: HTMLInputElement): void {
    const kb = this.kb();
    const files = Array.from(input.files ?? []);
    input.value = '';
    if (!kb || files.length === 0) {
      return;
    }

    const problem = files
      .map((f) => validateKnowledgeFile(f, kb.supportedExtensions, Number(kb.maxFileBytes)))
      .find((p) => p !== null);
    if (problem) {
      this.error.set(problem);
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    from(files)
      .pipe(concatMap((file) => this.api.uploadKnowledgeDocument(this.projectId(), file)))
      .subscribe({
        error: (e: unknown) => {
          this.error.set(describeApiError(e));
          this.busy.set(false);
          this.load();
        },
        complete: () => {
          this.busy.set(false);
          this.load();
        },
      });
  }

  protected retry(document: KnowledgeDocument): void {
    this.api.retryKnowledgeDocument(this.projectId(), document.id).subscribe({
      next: () => this.load(),
      error: (e: unknown) => this.error.set(describeApiError(e)),
    });
  }

  protected remove(document: KnowledgeDocument): void {
    if (!confirm(`移除「${document.fileName}」？之後的問答不會再引用這份文件。`)) {
      return;
    }

    this.api.removeKnowledgeDocument(this.projectId(), document.id).subscribe({
      next: () => this.load(),
      error: (e: unknown) => this.error.set(describeApiError(e)),
    });
  }

  private load(): void {
    this.poll?.unsubscribe();
    this.api.getKnowledgeBase(this.projectId()).subscribe({
      next: (kb) => {
        this.kb.set(kb);
        // 有文件在處理中時每 2 秒更新一次狀態。
        if (hasPendingDocuments(kb.documents)) {
          this.poll = timer(2000).subscribe(() => this.load());
        }
      },
      error: (e: unknown) => this.error.set(describeApiError(e)),
    });
  }
}
