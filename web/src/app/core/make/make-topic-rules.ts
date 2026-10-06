import { AdminMakeTopic, SaveMakeTopicRequest } from '../api/api-types';

/** 與後端 `MakeTopic` 的欄位上限一致（Domain 驗證才是權威，這裡只為了即時提示）。 */
export const MAKE_TOPIC_NAME_MAX = 50;
export const MAKE_TOPIC_DESCRIPTION_MAX = 200;
export const MAKE_TOPIC_INSTRUCTIONS_MAX = 4000;

export interface MakeTopicDraft {
  name: string;
  description: string;
  instructions: string;
  isEnabled: boolean;
}

export const emptyMakeTopicDraft = (): MakeTopicDraft => ({
  name: '',
  description: '',
  instructions: '',
  isEnabled: true,
});

export function draftFromTopic(topic: AdminMakeTopic): MakeTopicDraft {
  return {
    name: topic.name,
    description: topic.description ?? '',
    instructions: topic.instructions,
    isEnabled: topic.isEnabled,
  };
}

/** 表單問題；沒有問題回 null。 */
export function makeTopicDraftProblem(draft: MakeTopicDraft): string | null {
  const name = draft.name.trim();
  if (!name) {
    return '請輸入主題名稱';
  }
  if (name.length > MAKE_TOPIC_NAME_MAX) {
    return `主題名稱最多 ${MAKE_TOPIC_NAME_MAX} 字`;
  }
  if (draft.description.trim().length > MAKE_TOPIC_DESCRIPTION_MAX) {
    return `說明最多 ${MAKE_TOPIC_DESCRIPTION_MAX} 字`;
  }
  const instructions = draft.instructions.trim();
  if (!instructions) {
    return '請輸入給 Agent 的建置指示';
  }
  if (instructions.length > MAKE_TOPIC_INSTRUCTIONS_MAX) {
    return `建置指示最多 ${MAKE_TOPIC_INSTRUCTIONS_MAX} 字`;
  }
  return null;
}

export function toSaveRequest(draft: MakeTopicDraft, sortOrder: number): SaveMakeTopicRequest {
  return {
    name: draft.name.trim(),
    description: draft.description.trim() || null,
    instructions: draft.instructions.trim(),
    sortOrder,
    isEnabled: draft.isEnabled,
  };
}

/** OpenAPI 把 int32 標成 `number | string`，統一轉成數字。 */
export const sortOrderOf = (topic: Pick<AdminMakeTopic, 'sortOrder'>): number =>
  Number(topic.sortOrder);

/** 依 SortOrder 排序（相同時依名稱），與後端列表順序一致。 */
export function sortTopics<T extends Pick<AdminMakeTopic, 'sortOrder' | 'name'>>(topics: T[]): T[] {
  return [...topics].sort(
    (a, b) => sortOrderOf(a) - sortOrderOf(b) || a.name.localeCompare(b.name),
  );
}

/** 新主題排在最後：目前最大值 + 10（留間隔方便之後插入）。 */
export function nextSortOrder(topics: Pick<AdminMakeTopic, 'sortOrder'>[]): number {
  return topics.reduce((max, t) => Math.max(max, sortOrderOf(t)), 0) + 10;
}

export interface SortOrderChange {
  id: string;
  sortOrder: number;
}

/**
 * 把第 `index` 個主題往上（-1）或往下（+1）移一格，回傳需要更新 SortOrder 的主題。
 * 兩者 SortOrder 相同時（例如手動設定成一樣）互換數值無效，所以改成重新編號整個列表。
 */
export function moveTopic(
  topics: Pick<AdminMakeTopic, 'id' | 'sortOrder' | 'name'>[],
  index: number,
  direction: -1 | 1,
): SortOrderChange[] {
  const sorted = sortTopics(topics);
  const target = index + direction;
  if (index < 0 || index >= sorted.length || target < 0 || target >= sorted.length) {
    return [];
  }
  const a = sorted[index];
  const b = sorted[target];
  if (sortOrderOf(a) !== sortOrderOf(b)) {
    return [
      { id: a.id, sortOrder: sortOrderOf(b) },
      { id: b.id, sortOrder: sortOrderOf(a) },
    ];
  }
  const reordered = [...sorted];
  reordered[index] = b;
  reordered[target] = a;
  return reordered
    .map((t, i) => ({ id: t.id, sortOrder: (i + 1) * 10, current: sortOrderOf(t) }))
    .filter((c) => c.sortOrder !== c.current)
    .map(({ id, sortOrder }) => ({ id, sortOrder }));
}
