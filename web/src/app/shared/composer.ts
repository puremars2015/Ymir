import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MakeTopic } from '../core/api/api-types';
import {
  ComposerSubmission,
  MAKE_COMMAND,
  makeTopicContent,
  parseMakeCommand,
  showsMakeHint,
} from '../core/make/make-command';
import { MakeTopicStore } from '../core/make/make-topic.store';

/**
 * ChatGPT 式輸入框：Enter 送出、Shift+Enter 換行；中文輸入法選字中（isComposing）按 Enter 不送出。
 * 執行中顯示「停止」。
 *
 * `/make` 指令：輸入 `/` 時提示 `/make`；只送出 `/make` 時不送給 Agent，改為顯示主題按鈕，
 * 點按鈕直接送出（Agent 會先問需求）；`/make <描述>` 照常送出，由後端請 Agent 判斷主題。
 */
@Component({
  selector: 'app-composer',
  imports: [FormsModule],
  template: `
    @if (pickerOpen()) {
      <section class="make-picker" aria-label="選擇要建置的主題">
        <header>
          <span>想建置什麼？選一個主題，Agent 會先問你幾個問題。</span>
          <button type="button" class="link close" (click)="closePicker()" aria-label="關閉">
            ×
          </button>
        </header>
        @if (topics.loading() && topics.topics().length === 0) {
          <p class="muted">載入中…</p>
        } @else if (topics.error()) {
          <p class="error">{{ topics.error() }}</p>
        } @else if (topics.topics().length === 0) {
          <p class="muted">管理員尚未設定主題，可以直接在 /make 後面描述要做什麼。</p>
        } @else {
          <div class="topics">
            @for (topic of topics.topics(); track topic.id) {
              <button
                type="button"
                class="topic"
                [disabled]="busy() || disabled()"
                (click)="chooseTopic(topic)"
              >
                <strong>{{ topic.name }}</strong>
                @if (topic.description) {
                  <span>{{ topic.description }}</span>
                }
              </button>
            }
          </div>
        }
      </section>
    } @else if (hint()) {
      <button type="button" class="make-hint" (click)="useMakeHint()">
        <code>{{ makeCommand }}</code>
        <span>建置小工具或網站（直接送出可選主題，或在後面描述要做什麼）</span>
      </button>
    }
    <form class="composer" (ngSubmit)="submit()">
      <textarea
        #input
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
    :host {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
    }
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
    .make-hint {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      text-align: left;
      background: var(--surface);
      color: var(--text);
      border: 1px solid var(--border);
      border-radius: 0.75rem;
      padding: 0.5rem 0.875rem;
      font-size: 0.875rem;
      code {
        color: var(--accent);
        font-weight: 600;
      }
      span {
        color: var(--text-muted);
      }
    }
    .make-picker {
      border: 1px solid var(--border);
      border-radius: 1rem;
      background: var(--surface);
      padding: 0.75rem;
      display: flex;
      flex-direction: column;
      gap: 0.625rem;
      header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 0.5rem;
        font-size: 0.875rem;
        color: var(--text-muted);
      }
      p {
        margin: 0;
        font-size: 0.875rem;
      }
    }
    .close {
      font-size: 1.25rem;
      line-height: 1;
      color: var(--text-muted);
    }
    .topics {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(12rem, 1fr));
      gap: 0.5rem;
    }
    .topic {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: 0.25rem;
      text-align: left;
      background: var(--surface-muted);
      color: var(--text);
      border: 1px solid var(--border);
      border-radius: 0.75rem;
      padding: 0.75rem;
      span {
        font-size: 0.8125rem;
        color: var(--text-muted);
      }
      &:hover:not(:disabled) {
        border-color: var(--accent);
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Composer {
  protected readonly topics = inject(MakeTopicStore);
  private readonly inputRef = viewChild<ElementRef<HTMLTextAreaElement>>('input');

  readonly placeholder = input('輸入訊息，或輸入 /make 建置小工具、網站');
  readonly busy = input(false);
  readonly disabled = input(false);
  readonly submitted = output<ComposerSubmission>();
  readonly stopped = output<void>();

  protected readonly makeCommand = MAKE_COMMAND;
  protected readonly text = signal('');
  protected readonly pickerOpen = signal(false);
  protected readonly hint = computed(() => showsMakeHint(this.text()));

  protected canSubmit(): boolean {
    return !this.disabled() && this.text().trim().length > 0;
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      this.submit();
    } else if (event.key === 'Escape' && this.pickerOpen()) {
      this.closePicker();
    }
  }

  protected submit(): void {
    const text = this.text().trim();
    if (!text || this.busy() || this.disabled()) {
      return;
    }
    if (parseMakeCommand(text).kind === 'picker') {
      this.text.set('');
      this.pickerOpen.set(true);
      this.topics.refresh();
      return;
    }
    this.pickerOpen.set(false);
    this.submitted.emit({ content: text, makeTopicId: null });
    this.text.set('');
  }

  protected chooseTopic(topic: MakeTopic): void {
    if (this.busy() || this.disabled()) {
      return;
    }
    this.pickerOpen.set(false);
    this.submitted.emit({ content: makeTopicContent(topic.name), makeTopicId: topic.id });
  }

  protected closePicker(): void {
    this.pickerOpen.set(false);
    this.inputRef()?.nativeElement.focus();
  }

  protected useMakeHint(): void {
    this.text.set(`${MAKE_COMMAND} `);
    this.inputRef()?.nativeElement.focus();
  }
}
