import { ModelOption } from '../api/api-types';
import { isSystemPromptTooLong, resolveModel, SYSTEM_PROMPT_MAX_LENGTH } from './model-selection';

const models: ModelOption[] = [
  { id: 'minimax', displayName: 'MiniMax', isDefault: true, supportsImages: false },
  { id: 'gpt-x', displayName: 'GPT X', isDefault: false, supportsImages: false },
];

describe('resolveModel', () => {
  it('prefers the conversation model, then the user preference, then the default', () => {
    expect(resolveModel(models, 'gpt-x', 'minimax')).toBe('gpt-x');
    expect(resolveModel(models, null, 'gpt-x')).toBe('gpt-x');
    expect(resolveModel(models, null, null)).toBe('minimax');
  });

  it('skips models that are no longer available', () => {
    expect(resolveModel(models, 'removed', 'also-removed')).toBe('minimax');
  });

  it('returns null when no models are loaded yet', () => {
    expect(resolveModel([], 'gpt-x', null)).toBeNull();
  });
});

describe('isSystemPromptTooLong', () => {
  it('ignores surrounding whitespace and uses the backend limit', () => {
    expect(isSystemPromptTooLong(`  ${'x'.repeat(SYSTEM_PROMPT_MAX_LENGTH)}  `)).toBe(false);
    expect(isSystemPromptTooLong('x'.repeat(SYSTEM_PROMPT_MAX_LENGTH + 1))).toBe(true);
  });
});
