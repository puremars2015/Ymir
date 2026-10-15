import {
  clampPanelWidth,
  draggedPanelWidth,
  keyboardPanelWidth,
  parseStoredPanelWidth,
  PEOPLE_PANEL_DEFAULT_WIDTH,
  PEOPLE_PANEL_MAX_WIDTH,
  PEOPLE_PANEL_MIN_WIDTH,
  PEOPLE_PANEL_STEP,
} from './panel-width';

describe('panel width', () => {
  it('clamps to the allowed range', () => {
    expect(clampPanelWidth(100)).toBe(PEOPLE_PANEL_MIN_WIDTH);
    expect(clampPanelWidth(5000)).toBe(PEOPLE_PANEL_MAX_WIDTH);
    expect(clampPanelWidth(300.4)).toBe(300);
    expect(clampPanelWidth(Number.NaN)).toBe(PEOPLE_PANEL_DEFAULT_WIDTH);
  });

  it('parses stored values defensively', () => {
    expect(parseStoredPanelWidth(null)).toBe(PEOPLE_PANEL_DEFAULT_WIDTH);
    expect(parseStoredPanelWidth('abc')).toBe(PEOPLE_PANEL_DEFAULT_WIDTH);
    expect(parseStoredPanelWidth('-20')).toBe(PEOPLE_PANEL_DEFAULT_WIDTH);
    expect(parseStoredPanelWidth('400')).toBe(400);
    expect(parseStoredPanelWidth('9999')).toBe(PEOPLE_PANEL_MAX_WIDTH);
  });

  it('widens when dragging the divider to the left', () => {
    expect(draggedPanelWidth(300, 1000, 900)).toBe(400);
    expect(draggedPanelWidth(300, 1000, 1100)).toBe(PEOPLE_PANEL_MIN_WIDTH);
  });

  it('supports keyboard resizing', () => {
    expect(keyboardPanelWidth(300, 'ArrowLeft')).toBe(300 + PEOPLE_PANEL_STEP);
    expect(keyboardPanelWidth(300, 'ArrowRight')).toBe(300 - PEOPLE_PANEL_STEP);
    expect(keyboardPanelWidth(300, 'Home')).toBe(PEOPLE_PANEL_MIN_WIDTH);
    expect(keyboardPanelWidth(300, 'End')).toBe(PEOPLE_PANEL_MAX_WIDTH);
    expect(keyboardPanelWidth(300, 'Enter')).toBeNull();
  });
});
