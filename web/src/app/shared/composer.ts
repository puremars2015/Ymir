import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

/**
 * ChatGPT 式輸入框：Enter 送出、Shift+Enter 換行；中文輸入法選字中（isComposing）按 Enter 不送出。
 * 執行中顯示「停止」。
 */
@Component({
  selector: 'app-composer',
  imports: [FormsModule],
  template: `
    <form class="composer" (ngSubmit)="submit()">
      <textarea
        name="prompt"
        rows="1"
        [ngModel]="text()"
        (ngModelChange)="text.set($event)"
        (keydown)="onKeydown($event)"
        [placeholder]="placeholder()"
        [disabled]="disabled()"
        aria-label="訊息"
        autocomplete="off"
      ></textarea>
      @if (busy()) {
        <button type="button" class="round stop" (click)="stopped.emit()" aria-label="停止">
          ■
        </button>
      } @else {
        <button type="submit" class="round" [disabled]="!canSubmit()" aria-label="送出">↑</button>
      }
    </form>
  `,
  styles: `
    .composer {
      display: flex;
      align-items: flex-end;
      gap: 0.5rem;
      padding: 0.5rem 0.5rem 0.5rem 1rem;
      border: 1px solid var(--border);
      border-radius: 1.5rem;
      background: var(--surface);
      box-shadow: 0 1px 6px rgb(0 0 0 / 6%);
    }
    textarea {
      flex: 1;
      border: none;
      background: transparent;
      resize: none;
      padding: 0.5rem 0;
      max-height: 12rem;
      field-sizing: content;
      font: inherit;
      color: var(--text);
      outline: none;
    }
    .round {
      width: 2.25rem;
      height: 2.25rem;
      padding: 0;
      border-radius: 50%;
      flex-shrink: 0;
      font-size: 1rem;
      line-height: 1;
    }
    .stop {
      background: var(--text);
      color: var(--surface);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Composer {
  readonly placeholder = input('輸入訊息');
  readonly busy = input(false);
  readonly disabled = input(false);
  readonly submitted = output<string>();
  readonly stopped = output<void>();

  protected readonly text = signal('');

  protected canSubmit(): boolean {
    return !this.disabled() && this.text().trim().length > 0;
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      this.submit();
    }
  }

  protected submit(): void {
    const text = this.text().trim();
    if (!text || this.busy() || this.disabled()) {
      return;
    }
    this.submitted.emit(text);
    this.text.set('');
  }
}
