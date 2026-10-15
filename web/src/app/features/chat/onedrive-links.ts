import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { Observable, Subscription, forkJoin } from 'rxjs';
import { ApiService, describeApiError } from '../../core/api/api.service';
import { OneDriveBrowserItem, OneDriveLinkedItem, OneDriveStatus } from '../../core/api/api-types';
import { OneDriveBrowser } from '../settings/onedrive-browser';

@Component({
  selector: 'app-onedrive-links',
  imports: [DatePipe, RouterLink, OneDriveBrowser],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (visible()) {
      <section class="links stack" aria-label="OneDrive 連結檔案">
        <h3>OneDrive 連結檔案</h3>
        <p class="muted small">
          選定的檔案會在下次執行前下載，專案內的對話共用連結。目前只下載，尚未自動寫回雲端。
        </p>
        @if (ready()) {
          <button
            type="button"
            class="secondary"
            [disabled]="busy()"
            (click)="pickerOpen.set(!pickerOpen())"
          >
            {{ pickerOpen() ? '關閉選擇器' : '從 OneDrive 加入' }}
          </button>
        } @else {
          <a routerLink="/settings">前往設定連結 OneDrive／檢查權限</a>
        }
        @if (pickerOpen() && ready()) {
          <label class="small"
            ><input
              #readonly
              type="checkbox"
              [checked]="newReadOnly()"
              (change)="newReadOnly.set(readonly.checked)"
              [disabled]="busy()"
            />新連結設為唯讀</label
          >
          <app-onedrive-browser
            [selectable]="true"
            [selectionDisabled]="busy()"
            (selected)="add($event)"
          />
        }
        @if (error(); as error) {
          <p class="error small" role="alert">{{ error }}</p>
        }
        <ul>
          @for (link of links(); track link.id) {
            <li class="stack">
              <strong>{{ link.name }}</strong>
              <code>{{ link.localPath }}</code>
              <span class="small"
                >{{ stateLabel(link) }}
                @if (link.conflictCount) {
                  · 本機衝突副本：{{ link.conflictCount }}
                }
                @if (link.lastDownloadedAt) {
                  · {{ link.lastDownloadedAt | date: 'M/d HH:mm' }}
                }
              </span>
              @if (link.lastError) {
                <p class="error small">{{ link.lastError }}</p>
              }
              <div class="actions">
                <label class="small"
                  ><input
                    #readonly
                    type="checkbox"
                    [checked]="link.readOnly"
                    (change)="setReadOnly(link, readonly)"
                    [disabled]="busy() || !ready()"
                    [attr.aria-label]="link.name + ' 唯讀'"
                  />唯讀</label
                >
                <button
                  type="button"
                  class="link small"
                  [disabled]="busy()"
                  (click)="remove(link.id)"
                >
                  取消連結
                </button>
              </div>
            </li>
          } @empty {
            <li class="muted small">尚未連結檔案或資料夾。</li>
          }
        </ul>
        <p class="muted small">
          取消連結不會刪除雲端檔案或本機副本。機密文件應依管理政策關閉 Agent 對外連線。
        </p>
      </section>
    }
  `,
  styles: `
    .links {
      padding: 0.75rem;
      border: 1px solid var(--border);
      border-radius: 0.5rem;
    }
    h3,
    p {
      margin: 0;
    }
    h3 {
      font-size: 0.875rem;
    }
    .small {
      font-size: 0.75rem;
    }
    ul {
      list-style: none;
      padding: 0;
      margin: 0;
    }
    li {
      padding: 0.5rem 0;
      overflow-wrap: anywhere;
    }
    code {
      white-space: pre-wrap;
      font-size: 0.75rem;
    }
    .actions {
      display: flex;
      gap: 1rem;
      align-items: center;
    }
  `,
})
export class OneDriveLinks {
  private readonly api = inject(ApiService);
  readonly conversationId = input.required<string>();
  readonly reloading = input(false);
  protected readonly links = signal<OneDriveLinkedItem[]>([]);
  private readonly status = signal<OneDriveStatus | null>(null);
  protected readonly busy = signal(false);
  protected readonly pickerOpen = signal(false);
  protected readonly newReadOnly = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly ready = computed(
    () =>
      this.status()?.allowed === true &&
      this.status()?.available === true &&
      this.status()?.state === 'Connected',
  );
  protected readonly visible = computed(
    () => this.status()?.allowed === true || this.links().length > 0 || this.error() !== null,
  );
  private loadRequest: Subscription | null = null;
  private actionRequest: Subscription | null = null;
  private previousId: string | null = null;

  constructor() {
    effect(() => {
      const id = this.conversationId();
      const reloading = this.reloading();
      untracked(() => {
        if (id !== this.previousId) {
          this.loadRequest?.unsubscribe();
          this.actionRequest?.unsubscribe();
          this.previousId = id;
          this.links.set([]);
          this.status.set(null);
          this.error.set(null);
          this.pickerOpen.set(false);
          this.busy.set(false);
        }
        if (!reloading) this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.loadRequest?.unsubscribe();
      this.actionRequest?.unsubscribe();
    });
  }

  protected stateLabel(link: OneDriveLinkedItem): string {
    switch (link.state) {
      case 'Pending':
        return '等待下次執行下載';
      case 'Downloaded':
        return '已下載本機副本';
      case 'CloudDeleted':
        return '雲端已刪除或無法存取';
      default:
        return '下載未完成';
    }
  }

  protected add(item: OneDriveBrowserItem): void {
    this.act(this.api.addOneDriveLink(this.conversationId(), item.id, this.newReadOnly()), true);
  }

  protected setReadOnly(link: OneDriveLinkedItem, element: HTMLInputElement): void {
    const readOnly = element.checked;
    // 直到後端確認才改變顯示，避免失敗後 checkbox 看起來已切換成功。
    element.checked = link.readOnly;
    this.act(this.api.setOneDriveLinkReadOnly(this.conversationId(), link.id, readOnly));
  }

  protected remove(id: string): void {
    this.act(this.api.removeOneDriveLink(this.conversationId(), id));
  }

  private act(request: Observable<unknown>, closePicker = false): void {
    if (this.busy()) return;
    const id = this.conversationId();
    this.busy.set(true);
    this.error.set(null);
    this.actionRequest?.unsubscribe();
    this.actionRequest = request.subscribe({
      next: () => {
        if (id !== this.conversationId()) return;
        this.busy.set(false);
        if (closePicker) this.pickerOpen.set(false);
        this.load();
      },
      error: (error: unknown) => {
        if (id !== this.conversationId()) return;
        this.busy.set(false);
        this.error.set(describeApiError(error));
        this.load();
      },
    });
  }

  private load(): void {
    this.loadRequest?.unsubscribe();
    this.loadRequest = forkJoin({
      status: this.api.getOneDriveStatus(),
      links: this.api.getOneDriveLinks(this.conversationId()),
    }).subscribe({
      next: ({ status, links }) => {
        this.status.set(status);
        this.links.set(links);
        if (!this.ready()) this.pickerOpen.set(false);
      },
      error: (error: unknown) => this.error.set(describeApiError(error)),
    });
  }
}
