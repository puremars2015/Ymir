import { inject, Injectable, signal } from '@angular/core';
import { catchError, map, Observable, of, tap } from 'rxjs';
import { ApiService } from '../api/api.service';
import { Me, UserRole } from '../api/api-types';

/** 目前登入的使用者。前端不保存任何 token，登入狀態以 `/api/me` 為準（ADR-0002）。 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);
  private readonly current = signal<Me | null>(null);

  readonly user = this.current.asReadonly();

  /** 確認是否已登入；未登入回傳 false（不拋錯）。 */
  ensureLoaded(): Observable<boolean> {
    if (this.current()) {
      return of(true);
    }
    return this.api.me().pipe(
      tap((me) => this.current.set(me)),
      map(() => true),
      catchError(() => of(false)),
    );
  }

  devLogin(account: string, role: UserRole): Observable<Me> {
    return this.api.devLogin(account, null, role).pipe(tap((me) => this.current.set(me)));
  }

  logout(): Observable<void> {
    return this.api.logout().pipe(tap(() => this.current.set(null)));
  }

  /** 收到 401 時呼叫（cookie 過期等）。 */
  clear(): void {
    this.current.set(null);
  }
}
