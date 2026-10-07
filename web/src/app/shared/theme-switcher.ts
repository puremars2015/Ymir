import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ThemeStore } from '../core/theme/theme.store';

@Component({
  selector: 'app-theme-switcher',
  template: `
    <div class="theme-options" role="group" aria-label="顏色主題">
      @for (option of options; track option.mode) {
        <button
          type="button"
          [class.selected]="theme.mode() === option.mode"
          [attr.aria-pressed]="theme.mode() === option.mode"
          (click)="theme.select(option.mode)"
        >
          {{ option.label }}
        </button>
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
      margin: 0.5rem 0;
    }
    .theme-options {
      display: flex;
      gap: 0.25rem;
      padding: 0.25rem;
      border: 1px solid var(--border);
      border-radius: 10px;
      background: var(--surface-muted);
    }
    button {
      flex: 1;
      min-height: 2.25rem;
      padding: 0.375rem 0.5rem;
      font-size: 0.8125rem;
      background: transparent;
      color: var(--text-muted);
    }
    button:hover {
      background: var(--surface-active);
      color: var(--text);
    }
    button.selected {
      background: var(--accent);
      color: var(--accent-contrast);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ThemeSwitcher {
  protected readonly theme = inject(ThemeStore);
  protected readonly options = [
    { mode: 'light', label: '淺色' },
    { mode: 'dark', label: '深色' },
    { mode: 'auto', label: '自動' },
  ] as const;
}
