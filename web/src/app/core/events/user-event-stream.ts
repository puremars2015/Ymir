import { Injectable, signal } from '@angular/core';

/** `/api/events` 的事件名稱（ADR-0032 §2）；後端 UserEventEndpoints 同步修改。 */
export const USER_EVENT_TYPES = [
  'ready',
  'notification',
  'notification-read',
  'message',
  'read',
  'group-message',
  'group-updated',
  'group-read',
] as const;

export type UserEventType = (typeof USER_EVENT_TYPES)[number];

type Listener = (data: string) => void;

/**
 * 使用者事件串流（ADR-0032 §2）：每個分頁只開一條 `/api/events`，通知、私訊與群組事件都在這條連線上，
 * 避免 HTTP/1.1 每個網域 6 條連線的上限。認證靠同源 cookie（ADR-0002），所以直接用原生 EventSource；
 * 斷線時瀏覽器會自動重連，各 store 在 `ready` 時重新讀取資料補齊。私訊與群組事件由伺服器依角色過濾（訪客收不到）。
 */
@Injectable({ providedIn: 'root' })
export class UserEventStream {
  private source: EventSource | null = null;
  private userId: string | null = null;
  private readonly listeners = new Map<UserEventType, Set<Listener>>();

  readonly connected = signal(false);

  /** 瀏覽器不支援 EventSource（例如測試環境）時為 false：各 store 只在連線時讀取一次。 */
  readonly supported = typeof EventSource !== 'undefined';

  /** 登入後呼叫；同一位使用者重複呼叫無作用。 */
  connect(userId: string): void {
    if (this.source && this.userId === userId) return;
    this.disconnect();
    this.userId = userId;
    if (!this.supported) return;
    const source = new EventSource('/api/events', { withCredentials: true });
    this.source = source;
    // zoneless：signal 更新即會觸發畫面更新，不需要 NgZone。
    for (const type of USER_EVENT_TYPES) {
      source.addEventListener(type, (event: MessageEvent<string>) => {
        if (type === 'ready') this.connected.set(true);
        for (const listener of this.listeners.get(type) ?? []) listener(event.data);
      });
    }
    source.onerror = () => this.connected.set(false);
  }

  /** 登出時關閉連線；已註冊的 listener 保留，下次登入繼續使用。 */
  disconnect(): void {
    this.source?.close();
    this.source = null;
    this.userId = null;
    this.connected.set(false);
  }

  /** 訂閱某種事件，回傳取消訂閱的函式。 */
  on(type: UserEventType, listener: Listener): () => void {
    const set = this.listeners.get(type) ?? new Set<Listener>();
    set.add(listener);
    this.listeners.set(type, set);
    return () => set.delete(listener);
  }
}
