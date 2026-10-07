import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { Subscription } from 'rxjs';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { ArtifactGroup, WorkspaceFile } from '../../core/api/api-types';
import { Markdown } from '../../shared/markdown';
import { ArtifactDownload } from '../../shared/artifact-download';
import { OneDriveSync } from './onedrive-sync';
import { artifactDownloadUrl } from '../../core/files/artifact-files';
import {
  archiveDownloadUrl,
  fileDirectory,
  fileDownloadUrl,
  fileIcon,
  fileName,
  formatSize,
  PreviewKind,
  previewKind,
} from '../../core/files/workspace-files';

interface Preview {
  file: WorkspaceFile;
  executionId?: string;
  kind: PreviewKind;
  loading: boolean;
  text?: string;
  imageUrl?: string;
  error?: string;
}

/**
 * 對話的檔案（Agent 產生的成果）：預覽、逐一下載或打包成 zip。
 * 專案內的對話共用同一組檔案（ADR-0007）。檔案一律以附件下載，不在 Ymir 網域上開啟；
 * 預覽只以文字綁定顯示原始碼（HTML 不執行）、Markdown 經既有的 sanitizer、圖片用 blob URL。
 */
@Component({
  selector: 'app-files-panel',
  imports: [DatePipe, Markdown, ArtifactDownload, OneDriveSync],
  template: `
    <aside class="panel" aria-label="檔案">
      <header>
        <h2>檔案</h2>
        <button type="button" class="link" (click)="refresh.emit()" [disabled]="loading()">
          重新整理
        </button>
        <button type="button" class="link close" aria-label="關閉檔案面板" (click)="closed.emit()">
          ×
        </button>
      </header>
      <nav aria-label="檔案分類">
        <button
          type="button"
          class="button secondary small"
          [attr.aria-pressed]="tab() === 'artifacts'"
          (click)="selectTab('artifacts')"
        >
          成果
        </button>
        <button
          type="button"
          class="button secondary small"
          [attr.aria-pressed]="tab() === 'workspace'"
          (click)="selectTab('workspace')"
        >
          專案檔案
        </button>
      </nav>
      <app-onedrive-sync [conversationId]="conversationId()" [reloading]="loading()" />
      @if (tab() === 'workspace') {
        <p class="muted small">工作目錄內容，包含原始碼及上傳附件；這些檔案不一定是交付成果。</p>
      }
      @if (shared()) {
        <p class="muted small">這個專案的所有對話共用這些檔案。</p>
      }
      @if (error()) {
        <p class="error small">{{ error() }}</p>
      }
      @if (preview(); as p) {
        <section class="preview" aria-label="檔案預覽">
          <div class="preview-head">
            <button type="button" class="link" (click)="closePreview()">← 返回</button>
            <span class="name" [title]="p.file.path">{{ name(p.file) }}</span>
            <a class="button secondary small" [href]="downloadUrl(p.file, p.executionId)" download
              >⬇ 下載</a
            >
          </div>
          @if (p.loading) {
            <p class="muted small">載入中…</p>
          } @else if (p.error) {
            <p class="error small">{{ p.error }}</p>
          } @else {
            @switch (p.kind) {
              @case ('text') {
                <pre class="code">{{ p.text }}</pre>
              }
              @case ('markdown') {
                <div class="md"><app-markdown [text]="p.text ?? ''" /></div>
              }
              @case ('image') {
                <img class="image" [src]="p.imageUrl" [alt]="name(p.file)" />
              }
              @case ('too-large') {
                <p class="muted small">檔案太大，無法預覽，請下載。</p>
              }
              @default {
                <p class="muted small">這個類型無法預覽，請下載。</p>
              }
            }
          }
        </section>
      } @else if (tab() === 'artifacts') {
        @for (group of artifacts(); track group.executionId) {
          <section aria-label="交付成果">
            <h3>{{ group.createdAt | date: 'M/d HH:mm' }} 的成果</h3>
            <app-artifact-download [group]="group" [conversationId]="conversationId()" />
            <ul class="files">
              @for (file of group.files; track file.path) {
                <li class="file">
                  <button type="button" class="open" (click)="openPreview(file, group.executionId)">
                    <span class="icon" aria-hidden="true">{{ icon(file) }}</span>
                    <span class="meta"
                      ><span class="name">{{ file.path }}</span
                      ><span class="muted small">{{ size(file) }}</span></span
                    >
                  </button>
                  <a
                    class="download"
                    [href]="downloadUrl(file, group.executionId)"
                    download
                    [attr.aria-label]="'下載 ' + file.path"
                    >⬇</a
                  >
                </li>
              }
            </ul>
          </section>
        } @empty {
          <p class="muted small">
            {{ loading() ? '載入中…' : '尚無交付成果。閱讀或分析附件的回覆會直接顯示在對話中。' }}
          </p>
        }
      } @else if (files().length > 0) {
        <a class="button primary archive" [href]="archiveUrl()" download>⬇ 下載專案檔案（ZIP）</a>
        <ul class="files">
          @for (file of files(); track file.path) {
            <li class="file">
              <button
                type="button"
                class="open"
                [title]="'預覽 ' + file.path"
                (click)="openPreview(file)"
              >
                <span class="icon" aria-hidden="true">{{ icon(file) }}</span>
                <span class="meta">
                  <span class="name">{{ name(file) }}</span>
                  <span class="muted small">
                    @if (directory(file); as dir) {
                      {{ dir }} ·
                    }
                    {{ size(file) }} · {{ file.modifiedAt | date: 'M/d HH:mm' }}
                  </span>
                </span>
              </button>
              <a
                class="download"
                [href]="downloadUrl(file)"
                download
                [title]="'下載 ' + file.path"
                [attr.aria-label]="'下載 ' + name(file)"
                >⬇</a
              >
            </li>
          }
        </ul>
        @if (truncated()) {
          <p class="muted small">檔案太多，只列出前 1000 個；可用「全部下載」或請 Agent 整理。</p>
        }
      } @else if (loading()) {
        <p class="muted small">載入中…</p>
      } @else {
        <p class="muted small">還沒有檔案。請 Agent 建立檔案後，會出現在這裡。</p>
      }
    </aside>
  `,
  styles: `
    :host {
      display: block;
    }
    .panel {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
      height: 100%;
      box-sizing: border-box;
      padding: 1rem;
      overflow-y: auto;
      background: var(--surface);
      border-left: 1px solid var(--border);
    }
    header {
      display: flex;
      align-items: center;
      gap: 0.75rem;
    }
    h2 {
      margin: 0 auto 0 0;
      font-size: 1rem;
    }
    p {
      margin: 0;
    }
    .small {
      font-size: 0.8125rem;
    }
    .close {
      font-size: 1.25rem;
      line-height: 1;
      color: var(--text-muted);
    }
    .archive {
      text-align: center;
      text-decoration: none;
    }
    .files {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }
    .file {
      display: flex;
      align-items: center;
      gap: 0.25rem;
      border-radius: 0.5rem;
    }
    .file:hover {
      background: var(--surface-muted);
    }
    .open {
      flex: 1;
      min-width: 0;
      display: flex;
      align-items: center;
      gap: 0.625rem;
      padding: 0.5rem 0.625rem;
      background: none;
      color: var(--text);
      text-align: left;
      font: inherit;
    }
    .open:hover:not(:disabled) {
      background: none;
    }
    .meta {
      display: flex;
      flex-direction: column;
      min-width: 0;
      flex: 1;
    }
    .name {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .download {
      padding: 0.5rem 0.625rem;
      color: var(--accent);
      text-decoration: none;
    }
    .preview {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
      min-height: 0;
    }
    .preview-head {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .preview-head .name {
      flex: 1;
      font-weight: 600;
    }
    .preview-head .button {
      text-decoration: none;
      padding: 0.25rem 0.625rem;
    }
    .code {
      margin: 0;
      padding: 0.75rem;
      border-radius: 0.5rem;
      background: var(--surface-muted);
      font-size: 0.8125rem;
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }
    .image {
      max-width: 100%;
      border-radius: 0.5rem;
      border: 1px solid var(--border);
      background: repeating-conic-gradient(#0000000d 0 25%, #0000 0 50%) 0 0 / 16px 16px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FilesPanel {
  private readonly api = inject(ApiService);

  readonly conversationId = input.required<string>();
  readonly files = input.required<WorkspaceFile[]>();
  readonly artifacts = input<ArtifactGroup[]>([]);
  protected readonly tab = signal<'artifacts' | 'workspace'>('artifacts');
  readonly truncated = input(false);
  readonly loading = input(false);
  readonly error = input<string | null>(null);
  readonly shared = input(false);
  readonly refresh = output<void>();
  readonly closed = output<void>();

  protected readonly preview = signal<Preview | null>(null);
  private request: Subscription | null = null;

  constructor() {
    // 換對話時關閉預覽
    effect(() => {
      this.conversationId();
      untracked(() => {
        this.closePreview();
        this.tab.set('artifacts');
      });
    });
    inject(DestroyRef).onDestroy(() => this.closePreview());
  }

  protected selectTab(tab: 'artifacts' | 'workspace'): void {
    this.closePreview();
    this.tab.set(tab);
  }

  protected openPreview(file: WorkspaceFile, executionId?: string): void {
    this.closePreview();
    const kind = previewKind(file.path, file.size);
    const url = this.downloadUrl(file, executionId);
    if (kind === 'text' || kind === 'markdown') {
      this.preview.set({ file, executionId, kind, loading: true });
      this.request = this.api.fetchFileText(url).subscribe({
        next: (text) => this.preview.set({ file, executionId, kind, loading: false, text }),
        error: (e: unknown) =>
          this.preview.set({ file, executionId, kind, loading: false, error: describeApiError(e) }),
      });
    } else if (kind === 'image') {
      this.preview.set({ file, executionId, kind, loading: true });
      this.request = this.api.fetchFileBlob(url).subscribe({
        next: (blob) =>
          this.preview.set({
            file,
            executionId,
            kind,
            loading: false,
            imageUrl: URL.createObjectURL(imageBlob(blob, file.path)),
          }),
        error: (e: unknown) =>
          this.preview.set({ file, executionId, kind, loading: false, error: describeApiError(e) }),
      });
    } else {
      this.preview.set({ file, executionId, kind, loading: false });
    }
  }

  /** 關閉時釋放 blob URL。 */
  protected closePreview(): void {
    this.request?.unsubscribe();
    this.request = null;
    const imageUrl = this.preview()?.imageUrl;
    if (imageUrl) {
      URL.revokeObjectURL(imageUrl);
    }
    this.preview.set(null);
  }

  protected archiveUrl(): string {
    return archiveDownloadUrl(this.conversationId());
  }

  protected downloadUrl(file: WorkspaceFile, executionId?: string): string {
    return executionId
      ? artifactDownloadUrl(this.conversationId(), executionId, file.path)
      : fileDownloadUrl(this.conversationId(), file.path);
  }

  protected name(file: WorkspaceFile): string {
    return fileName(file.path);
  }

  protected directory(file: WorkspaceFile): string {
    return fileDirectory(file.path);
  }

  protected size(file: WorkspaceFile): string {
    return formatSize(file.size);
  }

  protected icon(file: WorkspaceFile): string {
    return fileIcon(file.path);
  }
}

/** 下載端點一律回 octet-stream；SVG 需要正確的 MIME 才能在 <img> 顯示。 */
function imageBlob(blob: Blob, path: string): Blob {
  return path.toLowerCase().endsWith('.svg') ? new Blob([blob], { type: 'image/svg+xml' }) : blob;
}
