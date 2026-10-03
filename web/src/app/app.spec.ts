import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { AuthService } from './core/auth/auth.service';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('renders the product name', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('.brand')?.textContent).toContain(
      'Vibe Maker',
    );
  });

  it('hides the user menu when not logged in', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    expect(TestBed.inject(AuthService).user()).toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('.user')).toBeNull();
  });
});
