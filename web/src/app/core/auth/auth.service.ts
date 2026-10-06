import { computed, inject, Injectable, signal } from '@angular/core';
import { catchError, map, Observable, of, tap } from 'rxjs';
import { ApiService } from '../api/api.service';
import { Me, UserRole } from '../api/api-types';

/** 目前登入的使用者。前端不保存任何 token，登入狀態以 `/api/me` 為準（ADR-0002）。 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);
  private readonly current = signal<Me | null>(null);

  readonly user = this.current.asReadonly();
  readonly isAdmin = computed(() => this.current()?.role === 'Admin');
  /** 本機帳號第一次登入（或被重設密碼）後必須先改密碼（ADR-0009）。 */
  readonly mustChangePassword = computed(() => this.current()?.mustChangePassword === true);

  /** 確認是否已登入；未登入回傳 false（不拋錯）。 */
  ensureLoaded(): Observable<boolean> {
    if (this.current()) {
      return of(true);
    }
    return this.reload();
  }

  /** 重新讀取 `/api/me`（例如改完密碼後）。 */
  reload(): Observable<boolean> {
    return this.api.me().pipe(
      tap((me) => this.current.set(me)),
      map(() => true),
      catchError(() => of(false)),
    );
  }

  passwordLogin(account: string, password: string): Observable<Me> {
    return this.api.passwordLogin(account, password).pipe(tap((me) => this.current.set(me)));
  }

  devLogin(account: string, role: UserRole): Observable<Me> {
    return this.api.devLogin(account, null, role).pipe(tap((me) => this.current.set(me)));
  }

  changePassword(currentPassword: string, newPassword: string): Observable<void> {
    return this.api
      .changePassword(currentPassword, newPassword)
      .pipe(
        tap(() => this.current.update((me) => (me ? { ...me, mustChangePassword: false } : me))),
      );
  }

  logout(): Observable<void> {
    return this.api.logout().pipe(tap(() => this.current.set(null)));
  }

  /** 收到 401 時呼叫（cookie 過期、帳號被停用等）。 */
  clear(): void {
    this.current.set(null);
  }
}
