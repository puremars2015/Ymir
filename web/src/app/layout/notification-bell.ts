import { ChangeDetectionStrategy, Component, ElementRef, inject, signal } from '@angular/core';
import { Notification } from '../core/api/api-types';
import { formatMessageTime } from '../core/messaging/direct-message-state';
import { NotificationStore } from '../core/notifications/notification.store';

/**
 * 頂列的通知鈴鐺（ADR-0032 N1）：未讀數、最近的通知、全部標為已讀，以及啟用瀏覽器通知。
 * 內容一律以文字綁定顯示（不經 innerHTML），點選只前往伺服器產生的站內連結。
 */
@Component({
  selector: 'app-notification-bell',
  host: {
    '(document:click)': 'closeOnOutsideClick($event)',
    '(document:keydown.escape)': 'open.set(false)',
  },
  template: `
    <button
      type="button"
      class="icon bell"
      (click)="toggle()"
      [attr.aria-label]="
        '通知' + (store.unreadCount() > 0 ? '，' + store.unreadCount() + ' 則未讀' : '')
      "
      title="通知"
      aria-haspopup="true"
      [attr.aria-expanded]="open()"
      aria-controls="notification-popover"
    >
      <svg viewBox="0 0 24 24" aria-hidden="true">
        <path d="M18 8a6 6 0 1 0-12 0c0 7-3 9-3 9h18s-3-2-3-9M13.73 21a2 2 0 0 1-3.46 0" />
      </svg>
      @if (store.unreadCount() > 0) {
        <span class="badge" aria-hidden="true">{{
          store.unreadCount() > 99 ? '99+' : store.unreadCount()
        }}</span>
      }
    </button>
    @if (open()) {
      <section id="notification-popover" class="popover" aria-label="通知">
        <header>
          <strong>通知</strong>
          @if (store.unreadCount() > 0) {
            <button type="button" class="link" (click)="store.markAllRead()">全部標為已讀</button>
          }
        </header>
        @if (store.canAskPermission()) {
          <p class="hint">
            <button type="button" class="link" (click)="store.requestPermission()">啟用瀏覽器通知</button
            ><span>，Ymir 在背景時，長時間的工作完成也會跳出提醒。</span>
          </p>
        }
        @if (store.error(); as error) {
          <p class="error small">{{ error }}</p>
        }
        <ul>
          @for (item of store.items(); track item.id) {
            <li>
              <button
                type="button"
                class="item"
                [class.unread]="!item.readAt"
                [class.failed]="item.kind === 'ExecutionFailed'"
                (click)="select(item)"
              >
                <span class="title">{{ item.title }}</span>
                @if (item.body) {
                  <span class="body">{{ item.body }}</span>
                }
                <time [attr.datetime]="item.createdAt">{{ time(item.createdAt) }}</time>
              </button>
            </li>
          } @empty {
            <li class="empty">目前沒有通知。長時間的工作完成或失敗時會通知你。</li>
          }
        </ul>
        @if (store.hasMore()) {
          <button
            type="button"
            class="link more"
            [disabled]="store.loading()"
            (click)="store.loadOlder()"
          >
            載入更早的通知
          </button>
        }
      </section>
    }
  `,
  styles: `
    /* 頂列靠右；成員面板按鈕（有的話）接在後面 */
    :host {
      position: relative;
      display: block;
      margin-left: auto;
    }
    .bell {
      position: relative;
      display: grid;
      place-items: center;
      width: 2.625rem;
      height: 2.625rem;
      border: 1px solid var(--border);
      border-radius: 0.75rem;
      color: var(--text);
      svg {
        width: 1.25rem;
        height: 1.25rem;
        fill: none;
        stroke: currentColor;
        stroke-width: 2;
        stroke-linecap: round;
        stroke-linejoin: round;
      }
    }
    .badge {
      position: absolute;
      top: -0.375rem;
      right: -0.375rem;
      min-width: 1.125rem;
      padding: 0 0.3125rem;
      border-radius: 999px;
      background: var(--danger);
      color: #fff;
      font-size: 0.625rem;
      font-weight: 700;
      line-height: 1.5;
    }
    .popover {
      position: absolute;
      top: calc(100% + 0.5rem);
      right: 0;
      z-index: 30;
      width: min(22rem, calc(100vw - 2rem));
      max-height: min(28rem, 70vh);
      overflow-y: auto;
      padding: 0.75rem;
      border: 1px solid var(--border);
      border-radius: 0.75rem;
      background: var(--surface);
      box-shadow: 0 0.5rem 1.5rem rgb(0 0 0 / 0.15);
    }
    header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 0.5rem;
    }
    ul {
      display: grid;
      gap: 0.25rem;
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .item {
      display: grid;
      gap: 0.125rem;
      width: 100%;
      padding: 0.5rem 0.625rem;
      border: 0;
      border-radius: 0.5rem;
      background: transparent;
      color: var(--text);
      text-align: left;
      &:hover,
      &:focus-visible {
        background: var(--surface-muted);
      }
      &.unread .title {
        font-weight: 700;
      }
      &.failed .title {
        color: var(--danger);
      }
    }
    .body {
      color: var(--text-muted);
      font-size: 0.8125rem;
      overflow-wrap: anywhere;
    }
    time,
    .hint,
    .empty {
      color: var(--text-muted);
      font-size: 0.75rem;
    }
    .hint {
      margin: 0 0 0.5rem;
    }
    .empty {
      padding: 0.75rem 0.25rem;
    }
    .more {
      margin-top: 0.5rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotificationBell {
  protected readonly store = inject(NotificationStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  protected readonly open = signal(false);

  protected toggle(): void {
    this.open.update((value) => !value);
  }

  protected select(item: Notification): void {
    this.open.set(false);
    this.store.open(item);
  }

  protected time(iso: string): string {
    return formatMessageTime(iso);
  }

  protected closeOnOutsideClick(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node))
      this.open.set(false);
  }
}
