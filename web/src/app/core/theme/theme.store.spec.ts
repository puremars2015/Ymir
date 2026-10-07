import { TestBed } from '@angular/core/testing';
import { ThemeStore } from './theme.store';

describe('ThemeStore', () => {
  beforeEach(() => {
    localStorage.removeItem('ymir.theme');
    delete document.documentElement.dataset['theme'];
  });

  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.removeItem('ymir.theme');
    delete document.documentElement.dataset['theme'];
  });

  it('defaults to automatic mode and ignores invalid saved values', () => {
    localStorage.setItem('ymir.theme', 'invalid');
    const theme = TestBed.inject(ThemeStore);
    expect(theme.mode()).toBe('auto');
    expect(document.documentElement.dataset['theme']).toBe('auto');
  });

  it('restores the explicit preference when the app starts', () => {
    localStorage.setItem('ymir.theme', 'dark');
    const theme = TestBed.inject(ThemeStore);
    expect(theme.mode()).toBe('dark');
    expect(document.documentElement.dataset['theme']).toBe('dark');
  });

  it('applies and saves each mode, including returning to automatic', () => {
    const theme = TestBed.inject(ThemeStore);
    for (const mode of ['dark', 'light', 'auto'] as const) {
      theme.select(mode);
      expect(theme.mode()).toBe(mode);
      expect(document.documentElement.dataset['theme']).toBe(mode);
      expect(localStorage.getItem('ymir.theme')).toBe(mode);
    }
  });

  it('still allows switching when the browser blocks storage', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('Storage blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('Storage blocked');
    });
    const theme = TestBed.inject(ThemeStore);
    expect(theme.mode()).toBe('auto');
    theme.select('light');
    expect(document.documentElement.dataset['theme']).toBe('light');
  });
});
