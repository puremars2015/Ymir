import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from './auth.service';

/** 未登入導向登入頁，並記住原本要去的位置；必須先改密碼時導向改密碼頁（ADR-0009）。 */
export const authGuard: CanActivateFn = (_route, state) => {
  const router = inject(Router);
  const auth = inject(AuthService);
  return auth.ensureLoaded().pipe(
    map((loggedIn) => {
      if (!loggedIn) {
        return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
      }
      return auth.mustChangePassword() ? router.createUrlTree(['/change-password']) : true;
    }),
  );
};

/** 改密碼頁：只要求登入（不檢查是否必須改密碼，避免循環導向）。 */
export const signedInGuard: CanActivateFn = () => {
  const router = inject(Router);
  return inject(AuthService)
    .ensureLoaded()
    .pipe(map((loggedIn) => (loggedIn ? true : router.createUrlTree(['/login']))));
};

/** Admin 專用頁面；後端仍以 AdminOnly policy 檢查（SA §4）。 */
export const adminGuard: CanActivateFn = () => {
  const router = inject(Router);
  const auth = inject(AuthService);
  return auth.ensureLoaded().pipe(map(() => (auth.isAdmin() ? true : router.createUrlTree(['/']))));
};
