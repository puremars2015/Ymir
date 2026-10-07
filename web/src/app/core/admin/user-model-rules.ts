import { UserModelAccess } from '../api/api-types';

export type ModelGrant = 'Inherit' | 'Allow' | 'Deny';
export type ModelDraft = Record<string, ModelGrant>;

export function modelDraft(state: UserModelAccess): ModelDraft {
  return Object.fromEntries(
    state.models.map((m) => [m.id, m.override == null ? 'Inherit' : m.override ? 'Allow' : 'Deny']),
  );
}

export function modelOverrides(draft: ModelDraft): Record<string, boolean> {
  return Object.fromEntries(
    Object.entries(draft)
      .filter(([, value]) => value !== 'Inherit')
      .map(([id, value]) => [id, value === 'Allow']),
  );
}

export function modelEnabled(grant: ModelGrant, systemEnabled: boolean): boolean {
  return grant === 'Inherit' ? systemEnabled : grant === 'Allow';
}
