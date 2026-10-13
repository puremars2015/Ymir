import { describe, expect, it } from 'vitest';
import { withProvider } from './model-label';

describe('withProvider', () => {
  it('appends the provider for the admin console', () => {
    expect(withProvider('MiniMax M2.7', 'minimax.io')).toBe('MiniMax M2.7 (minimax.io)');
    expect(withProvider('GPT-6.1 Sol', ' ChatGPT OAuth ')).toBe('GPT-6.1 Sol (ChatGPT OAuth)');
  });

  it('keeps the name when no provider is configured', () => {
    expect(withProvider('MiniMax M3')).toBe('MiniMax M3');
    expect(withProvider('MiniMax M3', null)).toBe('MiniMax M3');
    expect(withProvider('MiniMax M3', '  ')).toBe('MiniMax M3');
  });
});
