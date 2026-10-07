import { TestBed } from '@angular/core/testing';
import { ThemeSwitcher } from './theme-switcher';

describe('ThemeSwitcher', () => {
  afterEach(() => {
    localStorage.removeItem('ymir.theme');
    delete document.documentElement.dataset['theme'];
  });

  it('exposes three keyboard-accessible buttons and marks only the selected mode', async () => {
    localStorage.removeItem('ymir.theme');
    const fixture = TestBed.createComponent(ThemeSwitcher);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[role="group"]')?.getAttribute('aria-label')).toBe('顏色主題');
    const buttons = [...element.querySelectorAll('button')];
    expect(buttons.map((button) => button.textContent?.trim())).toEqual(['淺色', '深色', '自動']);
    expect(buttons.map((button) => button.getAttribute('aria-pressed'))).toEqual([
      'false',
      'false',
      'true',
    ]);
    buttons[1].click();
    await fixture.whenStable();
    expect(document.documentElement.dataset['theme']).toBe('dark');
    expect(buttons.map((button) => button.getAttribute('aria-pressed'))).toEqual([
      'false',
      'true',
      'false',
    ]);
  });
});
