import { describe, expect, it } from 'vitest';
import { modelDraft, modelEnabled, modelOverrides } from './user-model-rules';

describe('user model permissions', () => {
  it('keeps inheritance distinct from explicit permission', () => {
    const draft = modelDraft({
      models: [
        { id: 'a', displayName: 'A', systemEnabled: true, override: null, enabled: true },
        { id: 'c', displayName: 'C', systemEnabled: false, override: true, enabled: true },
        { id: 'd', displayName: 'D', systemEnabled: true, override: false, enabled: false },
      ],
      defaultModelId: 'a',
      isValid: true,
      updatedAt: null,
    });
    expect(draft).toEqual({ a: 'Inherit', c: 'Allow', d: 'Deny' });
    expect(modelOverrides(draft)).toEqual({ c: true, d: false });
    expect(modelOverrides({ ...draft, c: 'Inherit', d: 'Inherit' })).toEqual({});
  });
  it('inherits changed defaults while explicit overrides stay fixed', () => {
    expect(modelEnabled('Inherit', true)).toBe(true);
    expect(modelEnabled('Inherit', false)).toBe(false);
    expect(modelEnabled('Allow', false)).toBe(true);
    expect(modelEnabled('Deny', true)).toBe(false);
  });
});
