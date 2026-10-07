import { ModelOption } from '../api/api-types';

export const effortLabels: Record<string, string> = {
  none: '關閉',
  minimal: '最低',
  low: '輕量',
  medium: '標準',
  high: '深入',
  xhigh: '更深入',
  max: '最高',
};

export function thinkingLevels(model: ModelOption | undefined): readonly string[] {
  return model?.thinking?.levels ?? (model?.supportsThinking ? ['low', 'medium', 'high'] : []);
}

export function effectiveThinking(
  model: ModelOption | undefined,
  level: string | null,
): string | null {
  return level !== null && thinkingLevels(model).includes(level) ? level : null;
}
