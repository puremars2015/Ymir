import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { ModelOption } from '../core/api/api-types';

/** 模型下拉選單（放在輸入框上方）。只有一個模型時仍顯示，讓使用者知道目前用哪個模型。 */
@Component({
  selector: 'app-model-picker',
  template: `
    @if (models().length) {
      <label class="picker">
        <span class="sr-only">模型</span>
        <select
          name="model"
          [value]="selected() ?? ''"
          (change)="changed.emit($any($event.target).value)"
          aria-label="選擇模型"
        >
          @for (model of models(); track model.id) {
            <option [value]="model.id" [selected]="model.id === selected()">
              {{ model.displayName }}
            </option>
          }
        </select>
      </label>
    }
  `,
  styles: `
    .picker {
      display: inline-flex;
    }
    select {
      padding: 0.375rem 0.625rem;
      font-size: 0.875rem;
      border-radius: 999px;
      background: var(--surface-muted);
      border: 1px solid var(--border);
      cursor: pointer;
    }
    .sr-only {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip: rect(0 0 0 0);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ModelPicker {
  readonly models = input.required<readonly ModelOption[]>();
  readonly selected = input<string | null>(null);
  readonly changed = output<string>();
}
