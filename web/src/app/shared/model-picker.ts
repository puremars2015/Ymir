import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  Injector,
  input,
  output,
  signal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { ModelOption } from '../core/api/api-types';

/** 由圖示開啟模型與思考設定；保留 listbox 的鍵盤操作。 */
@Component({
  selector: 'app-model-picker',
  imports: [NgTemplateOutlet],
  host: { '(document:pointerdown)': 'onOutside($event)', '(window:resize)': 'close()' },
  template: `
    @if (current(); as model) {
      <button
        type="button"
        class="picker secondary"
        aria-label="模型與思考設定"
        title="模型與思考設定"
        aria-haspopup="dialog"
        [attr.aria-expanded]="open()"
        [attr.aria-controls]="listId + '-panel'"
        [attr.aria-description]="name(model)"
        (click)="toggle()"
        (keydown)="onTriggerKey($event)"
      >
        <svg class="settings-icon" viewBox="0 0 24 24" aria-hidden="true">
          <path d="M4 7h9m4 0h3M4 17h3m4 0h9" />
          <circle cx="15" cy="7" r="2" />
          <circle cx="9" cy="17" r="2" />
        </svg>
      </button>
      <span class="selection-summary">
        <span class="model-name">{{ name(model) }}</span>
        @if (model.supportsThinking) {
          <span class="depth-summary">· {{ depthLabel() }}</span>
        }
      </span>
      @if (open()) {
        <div
          class="settings-panel"
          role="dialog"
          aria-label="模型與思考設定"
          [id]="listId + '-panel'"
          [style.top.px]="position().top"
          [style.left.px]="position().left"
          [style.width.px]="position().width"
          [style.max-height.px]="position().height"
          (keydown.escape)="onEscape($event)"
          (focusout)="onFocusOut($event)"
        >
          <div class="section-title">選擇模型</div>
          <div
            class="model-list"
            role="listbox"
            aria-label="模型"
            tabindex="0"
            [id]="listId"
            [attr.aria-activedescendant]="listId + '-' + active()"
            (keydown)="onListKey($event)"
          >
            @for (option of models(); track option.id; let index = $index) {
              <button
                type="button"
                class="model-option secondary"
                role="option"
                tabindex="-1"
                [id]="listId + '-' + index"
                [attr.aria-label]="name(option)"
                [attr.aria-description]="
                  option.supportsImages ? '支援文字與圖片輸入' : '支援文字輸入'
                "
                [attr.aria-selected]="option.id === current()?.id"
                [class.active]="active() === index"
                (pointermove)="active.set(index)"
                (click)="choose(index)"
              >
                <span class="model-name">{{ name(option) }}</span>
                <ng-container
                  [ngTemplateOutlet]="modalities"
                  [ngTemplateOutletContext]="{ $implicit: option }"
                />
                <svg
                  class="check"
                  viewBox="0 0 24 24"
                  aria-hidden="true"
                  [class.visible]="option.id === current()?.id"
                >
                  <path d="m5 12 4 4L19 6" />
                </svg>
              </button>
            }
          </div>
          <fieldset class="thinking" [disabled]="!model.supportsThinking">
            <legend>思考深度</legend>
            <div class="depth-options">
              @for (level of levels; track level.value) {
                <label [class.selected]="effectiveDepth() === level.value">
                  <input
                    type="radio"
                    [name]="listId + '-depth'"
                    [value]="level.value ?? ''"
                    [checked]="effectiveDepth() === level.value"
                    (change)="depthChanged.emit(level.value)"
                  />
                  <span>{{ level.label }}</span>
                </label>
              }
            </div>
          </fieldset>
          <p class="depth-hint">
            {{
              model.supportsThinking
                ? '自動使用模型預設；較深的思考通常需要更多時間。'
                : '此模型未提供思考深度調整。'
            }}
          </p>
        </div>
      }
    }
    <ng-template #modalities let-model>
      <span class="modalities">
        <span class="modality text" role="img" aria-label="支援文字輸入" title="文字輸入">
          <svg viewBox="0 0 24 24" aria-hidden="true">
            <path d="M12 4v16M4 7V5a1 1 0 0 1 1-1h14a1 1 0 0 1 1 1v2M9 20h6" />
          </svg>
        </span>
        @if (model.supportsImages) {
          <span class="modality image" role="img" aria-label="支援圖片輸入" title="圖片輸入">
            <svg viewBox="0 0 24 24" aria-hidden="true">
              <rect x="3" y="3" width="18" height="18" rx="2" />
              <circle cx="9" cy="9" r="2" />
              <path d="m21 15-5-5L5 21" />
            </svg>
          </span>
        }
      </span>
    </ng-template>
  `,
  styleUrl: './model-picker.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ModelPicker {
  private static nextId = 0;
  readonly models = input.required<readonly ModelOption[]>();
  readonly selected = input<string | null>(null);
  readonly changed = output<string>();
  readonly depth = input<string | null>(null);
  readonly depthChanged = output<string | null>();
  protected readonly levels = [
    { value: null, label: '自動' },
    { value: 'low', label: '輕量' },
    { value: 'medium', label: '標準' },
    { value: 'high', label: '深入' },
  ];
  protected readonly effectiveDepth = computed(() =>
    this.current()?.supportsThinking ? this.depth() : null,
  );
  protected readonly depthLabel = computed(
    () => this.levels.find((l) => l.value === this.effectiveDepth())?.label ?? '自動',
  );
  protected readonly current = computed(
    () =>
      this.models().find((m) => m.id === this.selected()) ??
      this.models().find((m) => m.isDefault) ??
      this.models()[0],
  );
  protected readonly open = signal(false);
  protected readonly active = signal(0);
  protected readonly position = signal({ top: 0, left: 0, width: 280, height: 320 });
  protected readonly listId = `model-list-${ModelPicker.nextId++}`;
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  constructor() {
    const viewport = globalThis.window?.visualViewport;
    const close = () => this.close();
    viewport?.addEventListener('resize', close);
    viewport?.addEventListener('scroll', close);
    inject(DestroyRef).onDestroy(() => {
      viewport?.removeEventListener('resize', close);
      viewport?.removeEventListener('scroll', close);
    });
  }

  protected name(model: ModelOption): string {
    return model.displayName.replace(/\s*[（(]OpenRouter[）)]\s*$/i, '').trim();
  }

  protected toggle(): void {
    if (this.open()) {
      this.close();
      return;
    }
    this.active.set(
      Math.max(
        0,
        this.models().findIndex((m) => m.id === this.current()?.id),
      ),
    );
    const box = this.host.nativeElement.querySelector('button')!.getBoundingClientRect();
    const viewport = window.visualViewport;
    const top = viewport?.offsetTop ?? 0;
    const bottom = top + (viewport?.height ?? window.innerHeight);
    const width = Math.min(340, window.innerWidth - 24);
    const above = box.top - top - 16;
    const below = bottom - box.bottom - 16;
    const height = Math.min(480, this.models().length * 44 + 176, Math.max(above, below));
    this.position.set({
      top: above > below ? Math.max(top + 8, box.top - height - 8) : box.bottom + 8,
      left: Math.max(12, Math.min(box.left, window.innerWidth - width - 12)),
      width,
      height,
    });
    this.open.set(true);
    afterNextRender(
      () => {
        if (!this.open()) return;
        this.host.nativeElement.querySelector<HTMLElement>('.model-list')?.focus();
        this.revealActive();
      },
      { injector: this.injector },
    );
  }

  protected close(restoreFocus = false): void {
    this.open.set(false);
    if (restoreFocus) this.host.nativeElement.querySelector<HTMLButtonElement>('.picker')?.focus();
  }

  protected onOutside(event: Event): void {
    if (event.target instanceof Node && !this.host.nativeElement.contains(event.target))
      this.close();
  }

  protected onFocusOut(event: FocusEvent): void {
    if (event.relatedTarget instanceof Node) {
      if (!this.host.nativeElement.contains(event.relatedTarget)) this.close();
      return;
    }
    // 點擊 radio 的 label 時，Chrome 可能先送出 relatedTarget=null，再把焦點移到 input。
    setTimeout(() => {
      if (!this.host.nativeElement.contains(document.activeElement)) this.close();
    });
  }

  protected onTriggerKey(event: KeyboardEvent): void {
    if (this.open()) {
      this.onListKey(event);
      return;
    }
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      if (!this.open()) this.toggle();
    }
  }

  protected onListKey(event: KeyboardEvent): void {
    if (event.key === 'Tab') return;
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      this.close(true);
      return;
    }
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.choose(this.active());
      return;
    }
    let next = this.active();
    if (event.key === 'ArrowDown') next = (next + 1) % this.models().length;
    else if (event.key === 'ArrowUp')
      next = (next - 1 + this.models().length) % this.models().length;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = this.models().length - 1;
    else return;
    event.preventDefault();
    this.active.set(next);
    afterNextRender(() => this.revealActive(), { injector: this.injector });
  }

  private revealActive(): void {
    this.host.nativeElement
      .querySelector<HTMLElement>(`#${this.listId}-${this.active()}`)
      ?.scrollIntoView({ block: 'nearest' });
  }

  protected choose(index: number): void {
    const model = this.models()[index];
    if (model) this.changed.emit(model.id);
  }

  protected onEscape(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    this.close(true);
  }
}
