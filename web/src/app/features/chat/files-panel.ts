import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { WorkspaceFile } from '../../core/api/api-types';
import {
  archiveDownloadUrl,
  fileDirectory,
  fileDownloadUrl,
  fileIcon,
  fileName,
  formatSize,
} from '../../core/files/workspace-files';

/**
 * 對話的檔案（Agent 產生的成果）：逐一下載或打包成 zip。
 * 專案內的對話共用同一組檔案（ADR-0007）。檔案一律以附件下載，不在 Ymir 網域上開啟。
 */
@Component({
  selector: 'app-files-panel',
  imports: [DatePipe],
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
      @if (shared()) {
        <p class="muted small">這個專案的所有對話共用這些檔案。</p>
      }
      @if (error()) {
        <p class="error small">{{ error() }}</p>
      }
      @if (files().length > 0) {
        <a class="button primary archive" [href]="archiveUrl()" download>⬇ 全部下載（.zip）</a>
        <ul class="files">
          @for (file of files(); track file.path) {
            <li>
              <a class="file" [href]="downloadUrl(file)" download [title]="'下載 ' + file.path">
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
                <span class="download" aria-hidden="true">⬇</span>
              </a>
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
      gap: 0.625rem;
      padding: 0.5rem 0.625rem;
      border-radius: 0.5rem;
      color: var(--text);
      text-decoration: none;
    }
    .file:hover {
      background: var(--surface-muted);
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
      color: var(--accent);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FilesPanel {
  readonly conversationId = input.required<string>();
  readonly files = input.required<WorkspaceFile[]>();
  readonly truncated = input(false);
  readonly loading = input(false);
  readonly error = input<string | null>(null);
  readonly shared = input(false);
  readonly refresh = output<void>();
  readonly closed = output<void>();

  protected archiveUrl(): string {
    return archiveDownloadUrl(this.conversationId());
  }

  protected downloadUrl(file: WorkspaceFile): string {
    return fileDownloadUrl(this.conversationId(), file.path);
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
