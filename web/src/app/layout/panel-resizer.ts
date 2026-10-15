import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import {
  draggedPanelWidth,
  keyboardPanelWidth,
  PEOPLE_PANEL_DEFAULT_WIDTH,
  PEOPLE_PANEL_MAX_WIDTH,
  PEOPLE_PANEL_MIN_WIDTH,
} from './panel-width';

/**
 * 右側成員面板左緣的分隔線：拖曳（或聚焦後按左右鍵、Home / End）調整寬度，雙擊回到預設寬度。
 * 本身寬 0，以 ::before 擴大可拖曳範圍，不佔版面。寬度由 shell 保存與記住。
 */
@Component({
  selector: 'app-panel-resizer',
  template: '',
  host: {
    role: 'separator',
    'aria-orientation': 'vertical',
    'aria-label': '調整成員面板寬度',
    title: '拖曳調整寬度，雙擊還原',
    tabindex: '0',
    '[attr.aria-valuenow]': 'width()',
    '[attr.aria-valuemin]': 'min',
    '[attr.aria-valuemax]': 'max',
    '[class.active]': 'active()',
    '(pointerdown)': 'begin($event)',
    '(pointermove)': 'move($event)',
    '(pointerup)': 'end()',
    '(pointercancel)': 'end()',
    '(lostpointercapture)': 'end()',
    '(dblclick)': 'commit(defaultWidth)',
    '(keydown)': 'key($event)',
  },
  styles: `
    :host {
      position: relative;
      flex: 0 0 0;
      z-index: 5;
      cursor: col-resize;
      touch-action: none;
      outline: none;
    }
    :host::before {
      content: '';
      position: absolute;
      inset: 0 -5px;
    }
    :host::after {
      content: '';
      position: absolute;
      inset: 0 -1px;
      transition: background 0.15s;
    }
    :host(:hover)::after,
    :host(:focus-visible)::after,
    :host(.active)::after {
      background: var(--accent);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PanelResizer {
  /** 目前記住的寬度（px）。 */
  readonly width = input.required<number>();
  /** 拖曳中持續送出新寬度。 */
  readonly resized = output<number>();
  /** 拖曳結束或鍵盤調整後送出，shell 才記住。 */
  readonly committed = output<number>();
  readonly resizing = output<boolean>();

  protected readonly min = PEOPLE_PANEL_MIN_WIDTH;
  protected readonly max = PEOPLE_PANEL_MAX_WIDTH;
  protected readonly defaultWidth = PEOPLE_PANEL_DEFAULT_WIDTH;
  protected readonly active = signal(false);
  private start: { x: number; width: number; last: number } | null = null;

  protected begin(event: PointerEvent): void {
    if (event.button !== 0) return;
    const target = event.currentTarget as HTMLElement;
    // 以畫面上的實際寬度為起點（CSS 會依視窗寬度再限制，可能比記住的值窄）。
    const panel = target.nextElementSibling;
    const width = panel?.getBoundingClientRect().width || this.width();
    this.start = { x: event.clientX, width, last: this.width() };
    target.setPointerCapture?.(event.pointerId);
    this.active.set(true);
    this.resizing.emit(true);
    event.preventDefault();
  }

  protected move(event: PointerEvent): void {
    if (!this.start) return;
    this.start.last = draggedPanelWidth(this.start.width, this.start.x, event.clientX);
    this.resized.emit(this.start.last);
  }

  protected end(): void {
    if (!this.start) return;
    const width = this.start.last;
    this.start = null;
    this.active.set(false);
    this.resizing.emit(false);
    this.committed.emit(width);
  }

  protected key(event: KeyboardEvent): void {
    const width = keyboardPanelWidth(this.width(), event.key);
    if (width === null) return;
    event.preventDefault();
    this.commit(width);
  }

  protected commit(width: number): void {
    this.resized.emit(width);
    this.committed.emit(width);
  }
}
