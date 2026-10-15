/**
 * 右側成員面板的寬度（可拖曳調整並記住）。實際顯示時 CSS 另以視窗寬度限制，避免擠壓中間的對話區。
 */
export const PEOPLE_PANEL_DEFAULT_WIDTH = 288;
export const PEOPLE_PANEL_MIN_WIDTH = 256;
export const PEOPLE_PANEL_MAX_WIDTH = 640;
/** 鍵盤調整的步距（分隔線聚焦時按左右鍵）。 */
export const PEOPLE_PANEL_STEP = 16;

export function clampPanelWidth(width: number): number {
  if (!Number.isFinite(width)) return PEOPLE_PANEL_DEFAULT_WIDTH;
  return Math.round(Math.min(PEOPLE_PANEL_MAX_WIDTH, Math.max(PEOPLE_PANEL_MIN_WIDTH, width)));
}

/** 讀取記住的寬度；沒有或格式不對時用預設值。 */
export function parseStoredPanelWidth(value: string | null): number {
  if (!value || !/^\d+$/.test(value)) return PEOPLE_PANEL_DEFAULT_WIDTH;
  return clampPanelWidth(Number(value));
}

/** 面板在右側：分隔線往左拖（clientX 變小）面板變寬。 */
export function draggedPanelWidth(startWidth: number, startX: number, currentX: number): number {
  return clampPanelWidth(startWidth + (startX - currentX));
}

/** 分隔線的鍵盤操作；不處理的按鍵回傳 null。 */
export function keyboardPanelWidth(width: number, key: string): number | null {
  switch (key) {
    case 'ArrowLeft':
      return clampPanelWidth(width + PEOPLE_PANEL_STEP);
    case 'ArrowRight':
      return clampPanelWidth(width - PEOPLE_PANEL_STEP);
    case 'Home':
      return PEOPLE_PANEL_MIN_WIDTH;
    case 'End':
      return PEOPLE_PANEL_MAX_WIDTH;
    default:
      return null;
  }
}
