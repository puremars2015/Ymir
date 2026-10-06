import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  provideRouter,
  Router,
  RouterStateSnapshot,
  UrlTree,
} from '@angular/router';
import { firstValueFrom, Observable, of } from 'rxjs';
import { authGuard } from './auth.guard';
import { AuthService } from './auth.service';

describe('authGuard', () => {
  const run = (loggedIn: boolean, mustChangePassword = false) => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            ensureLoaded: () => of(loggedIn),
            mustChangePassword: () => mustChangePassword,
          },
        },
      ],
    });
    const result = TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, { url: '/conversations/abc' } as RouterStateSnapshot),
    );
    return firstValueFrom(result as Observable<boolean | UrlTree>);
  };

  it('allows logged-in users', async () => {
    expect(await run(true)).toBe(true);
  });

  it('redirects anonymous users to login with the return url', async () => {
    const result = await run(false);
    const router = TestBed.inject(Router);
    expect(result instanceof UrlTree).toBe(true);
    expect(router.serializeUrl(result as UrlTree)).toBe('/login?returnUrl=%2Fconversations%2Fabc');
  });

  it('sends users who must change their password to the change-password page', async () => {
    const result = await run(true, true);
    const router = TestBed.inject(Router);
    expect(router.serializeUrl(result as UrlTree)).toBe('/change-password');
  });
});
