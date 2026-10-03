import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { EXECUTION_EVENT_TYPES, ExecutionEvent, TERMINAL_EVENT_TYPES } from './execution-events';

/**
 * 以瀏覽器原生 EventSource 訂閱 execution 事件。
 * 認證採 BFF + Cookie（ADR-0002），EventSource 會自動帶同源 Cookie，不需要 Authorization header。
 */
@Injectable({ providedIn: 'root' })
export class ExecutionStreamService {
  stream(url: string): Observable<ExecutionEvent> {
    return new Observable<ExecutionEvent>((subscriber) => {
      const source = new EventSource(url, { withCredentials: true });
      let finished = false;

      for (const type of EXECUTION_EVENT_TYPES) {
        source.addEventListener(type, (message: MessageEvent<string>) => {
          const event = { type, data: JSON.parse(message.data) } as ExecutionEvent;
          subscriber.next(event);
          if (TERMINAL_EVENT_TYPES.has(type)) {
            finished = true;
            source.close();
            subscriber.complete();
          }
        });
      }

      source.onerror = () => {
        // 伺服器正常結束串流後 EventSource 會觸發 error 並嘗試重連；終止事件之後一律忽略。
        if (!finished) {
          source.close();
          subscriber.error(new Error('與伺服器的串流連線中斷'));
        }
      };

      return () => source.close();
    });
  }
}
