import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { ApiProblem } from '../api/api-types';
import { AuthService } from './auth.service';

/**
 * API 回 401（cookie 過期、帳號被停用）時清除登入狀態並導向登入頁；`/api/me` 與登入端點本身由呼叫端處理。
 * 回 403 `PASSWORD_CHANGE_REQUIRED` 時導向改密碼頁（ADR-0009）。
 */
export const unauthorizedInterceptor: HttpInterceptorFn = (request, next) => {
  const router = inject(Router);
  const auth = inject(AuthService);
  return next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse) {
        const isAuthCall = request.url.endsWith('/api/me') || request.url.includes('/api/auth/');
        if (error.status === 401 && !isAuthCall) {
          auth.clear();
          void router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
        } else if (
          error.status === 403 &&
          (error.error as ApiProblem | null)?.code === 'PASSWORD_CHANGE_REQUIRED'
        ) {
          void router.navigate(['/change-password']);
        }
      }
      return throwError(() => error);
    }),
  );
};
