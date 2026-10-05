import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { App } from './app';
import { routes } from './app.routes';

/** Router 的 guard / lazy load 是非同步的；讓出事件迴圈直到 HTTP 請求送出。 */
const tick = () => new Promise((resolve) => setTimeout(resolve));

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('sends anonymous users to the login page', async () => {
    const fixture = TestBed.createComponent(App);
    const http = TestBed.inject(HttpTestingController);
    const navigation = TestBed.inject(Router).navigateByUrl('/');
    await tick();
    http.expectOne('/api/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    await navigation;
    await fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/login?returnUrl=%2F');
  });

  it('opens the main layout with the sidebar right after login', async () => {
    const fixture = TestBed.createComponent(App);
    const http = TestBed.inject(HttpTestingController);
    const navigation = TestBed.inject(Router).navigateByUrl('/');
    await tick();
    http
      .expectOne('/api/me')
      .flush({ id: 'u1', displayName: 'Alice', role: 'User', status: 'ACTIVE' });
    await navigation;
    await fixture.whenStable();
    http.expectOne('/api/projects').flush([
      {
        id: 'p1',
        name: '行銷網站',
        status: 'ACTIVE',
        createdAt: '2026-10-01',
        updatedAt: '2026-10-01',
      },
    ]);
    http.expectOne('/api/conversations').flush([
      {
        id: 'c1',
        projectId: null,
        title: '隨便聊聊',
        status: 'ACTIVE',
        createdAt: '2026-10-02',
        updatedAt: '2026-10-02',
      },
    ]);
    await fixture.whenStable();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('.sidebar .brand')?.textContent).toContain('Vibe Maker');
    expect(page.querySelector('.project-link')?.textContent).toContain('行銷網站');
    expect(page.textContent).toContain('隨便聊聊');
    expect(page.querySelector('.user .name')?.textContent).toContain('Alice');
    expect(page.querySelector('app-new-chat-page textarea')).not.toBeNull();
  });
});
