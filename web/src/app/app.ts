import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

/** 根元件只放 router-outlet：登入頁是全頁，登入後由 {@link Shell} 提供 ChatGPT 式版面。 */
@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  template: '<router-outlet />',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {}
