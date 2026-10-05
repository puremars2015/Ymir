import { ModelOption } from '../api/api-types';

/** 與後端相同的 system prompt 上限（Project.SystemPromptMaxLength）。 */
export const SYSTEM_PROMPT_MAX_LENGTH = 10_000;

/**
 * 決定要顯示 / 使用的模型：對話上次用的 → 使用者上次選的 → 系統預設 → 第一個。
 * 已不在清單內的模型（例如設定被移除）會被略過。
 */
export function resolveModel(
  models: readonly ModelOption[],
  conversationModelId: string | null | undefined,
  preferredModelId: string | null | undefined,
): string | null {
  const available = (id: string | null | undefined): id is string =>
    !!id && models.some((m) => m.id === id);
  if (available(conversationModelId)) {
    return conversationModelId;
  }
  if (available(preferredModelId)) {
    return preferredModelId;
  }
  return models.find((m) => m.isDefault)?.id ?? models[0]?.id ?? null;
}

/** system prompt 是否超過上限（以後端相同的 UTF-16 長度計算，前後空白不計）。 */
export function isSystemPromptTooLong(text: string): boolean {
  return text.trim().length > SYSTEM_PROMPT_MAX_LENGTH;
}
