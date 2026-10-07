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
import { Subscription } from 'rxjs';
import { ApiService } from '../core/api/api.service';
import { Attachment } from '../core/api/api-types';
import { attachmentIcon, isImageType } from '../core/attachments/attachment-rules';
import {
  fileDownloadUrl,
  formatSize,
  MAX_IMAGE_PREVIEW_BYTES,
} from '../core/files/workspace-files';

/**
 * 訊息上的附件：圖片顯示縮圖、其他檔案顯示可下載的 chip。
 * 縮圖以 blob URL 的 `<img>` 顯示（不以 innerHTML / iframe 顯示使用者或 Agent 的內容）；下載一律走附件下載端點。
 */
@Component({
  selector: 'app-attachment-list',
  template: `
    <ul class="attachments" aria-label="附加的檔案">
      @for (attachment of attachments(); track attachment.id) {
        <li>
          @if (thumbnails()[attachment.id]; as url) {
            <a
              class="thumb"
              [href]="downloadUrl(attachment)"
              download
              [title]="attachment.fileName"
            >
              <img [src]="url" [alt]="attachment.fileName" />
            </a>
          } @else {
            <a class="chip" [href]="downloadUrl(attachment)" download [title]="attachment.path">
              <span aria-hidden="true">{{ icon(attachment) }}</span>
              <span class="name">{{ attachment.fileName }}</span>
              <span class="size">{{ size(attachment) }}</span>
            </a>
          }
        </li>
      }
    </ul>
  `,
  styles: `
    .attachments {
      list-style: none;
      margin: 0 0 0.375rem;
      padding: 0;
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: 0.375rem;
    }
    .thumb img {
      display: block;
      max-width: 12rem;
      max-height: 9rem;
      border-radius: 0.5rem;
      border: 1px solid var(--border);
      background: var(--surface);
      object-fit: cover;
    }
    .chip {
      display: flex;
      align-items: center;
      gap: 0.375rem;
      max-width: 16rem;
      padding: 0.25rem 0.625rem;
      border: 1px solid var(--border);
      border-radius: 999px;
      background: var(--surface);
      color: var(--text);
      font-size: 0.8125rem;
      text-decoration: none;
      .name {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .size {
        color: var(--text-muted);
        flex-shrink: 0;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AttachmentList {
  private readonly api = inject(ApiService);

  readonly conversationId = input.required<string>();
  readonly attachments = input.required<Attachment[]>();

  /** attachment id → blob URL。 */
  protected readonly thumbnails = signal<Record<string, string>>({});
  private requests: Subscription[] = [];

  constructor() {
    effect(() => {
      const conversationId = this.conversationId();
      const attachments = this.attachments();
      untracked(() => this.loadThumbnails(conversationId, attachments));
    });
    inject(DestroyRef).onDestroy(() => this.release());
  }

  protected downloadUrl(attachment: Attachment): string {
    return fileDownloadUrl(this.conversationId(), attachment.path);
  }

  protected icon(attachment: Attachment): string {
    return attachmentIcon(attachment.contentType);
  }

  protected size(attachment: Attachment): string {
    return formatSize(attachment.size);
  }

  private loadThumbnails(conversationId: string, attachments: Attachment[]): void {
    this.release();
    for (const attachment of attachments) {
      if (
        !isImageType(attachment.contentType) ||
        Number(attachment.size) > MAX_IMAGE_PREVIEW_BYTES
      ) {
        continue;
      }
      this.requests.push(
        this.api.fetchFileBlob(fileDownloadUrl(conversationId, attachment.path)).subscribe({
          next: (blob) => {
            // 下載端點一律回 octet-stream；以後端判斷的類型重新包裝，<img> 才能顯示（例如 SVG）。
            const url = URL.createObjectURL(new Blob([blob], { type: attachment.contentType }));
            this.thumbnails.update((thumbs) => ({ ...thumbs, [attachment.id]: url }));
          },
          error: () => undefined, // 檔案可能已被 Agent 刪除：改顯示一般的 chip
        }),
      );
    }
  }

  private release(): void {
    this.requests.forEach((r) => r.unsubscribe());
    this.requests = [];
    Object.values(this.thumbnails()).forEach((url) => URL.revokeObjectURL(url));
    this.thumbnails.set({});
  }
}
