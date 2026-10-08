import { ModelOption } from '../api/api-types';
import { effectiveThinking, thinkingLevels } from './thinking-options';

describe('per-model thinking capability', () => {
  const fixed: ModelOption = {
    id: 'fixed',
    displayName: 'Fixed',
    isDefault: true,
    supportsImages: false,
    supportsThinking: false,
    allowKnowledgeBase: false,
  };
  const luna: ModelOption = {
    ...fixed,
    id: 'luna',
    supportsThinking: true,
    allowKnowledgeBase: false,
    thinking: {
      parameter: 'reasoning_effort',
      levels: ['none', 'low', 'medium', 'high', 'xhigh', 'max'],
      defaultLevel: 'medium',
      required: false,
    },
  };
  const mandatory: ModelOption = {
    ...luna,
    id: 'mandatory',
    thinking: {
      ...luna.thinking!,
      levels: ['low', 'medium', 'high', 'xhigh', 'max'],
      required: true,
    },
  };

  it('shows no depth choices for a fixed-thinking model', () => {
    expect(thinkingLevels(fixed)).toEqual([]);
    expect(effectiveThinking(fixed, 'high')).toBeNull();
  });

  it('keeps explicit off distinct from provider default', () => {
    expect(effectiveThinking(luna, 'none')).toBe('none');
    expect(effectiveThinking(luna, null)).toBeNull();
    expect(effectiveThinking(mandatory, 'none')).toBeNull();
  });

  it('uses exactly the published levels without coercing unsupported preferences', () => {
    expect(thinkingLevels(luna)).toHaveLength(6);
    expect(effectiveThinking(luna, 'max')).toBe('max');
    expect(effectiveThinking(luna, 'minimal')).toBeNull();
    expect(effectiveThinking(undefined, 'high')).toBeNull();
  });
});
