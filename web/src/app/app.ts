import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ThemeStore } from './core/theme/theme.store';

/** 根元件只放 router-outlet：登入頁是全頁，登入後由 {@link Shell} 提供 ChatGPT 式版面。 */
@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  template: '<router-outlet />',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  constructor() {
    // 在登入頁也套用上次選擇，避免登入後才切換配色。
    inject(ThemeStore);
  }
}
