import { Conversation, Project } from '../api/api-types';

export const TITLE_MAX_LENGTH = 40;

/**
 * 像 ChatGPT 一樣直接開聊：以第一則訊息的第一個非空白行當對話標題，過長時截斷並加上「…」。
 * 後端標題上限 300 字（SA §8），這裡取更短的長度讓側邊欄好讀。
 */
export function deriveTitle(firstMessage: string, maxLength = TITLE_MAX_LENGTH): string {
  const line =
    firstMessage
      .split(/\r?\n/)
      .map((l) => l.trim().replace(/\s+/g, ' '))
      .find((l) => l.length > 0) ?? '';
  if (!line) {
    return '新對話';
  }
  const chars = Array.from(line); // 以字元（含 emoji）計算，避免切壞代理字元
  return chars.length > maxLength ? `${chars.slice(0, maxLength).join('')}…` : line;
}

export interface ProjectGroup {
  project: Project;
  conversations: Conversation[];
}

export interface ConversationGroups {
  projects: ProjectGroup[];
  ungrouped: Conversation[];
}

const byUpdatedDesc = (a: { updatedAt: string }, b: { updatedAt: string }) =>
  b.updatedAt.localeCompare(a.updatedAt);

/**
 * 側邊欄分組（ADR-0007）：每個專案底下列它的對話，沒有專案的對話放在「聊天」。
 * 皆依最近更新排序；指向不存在專案的對話（例如專案已封存）視為未分組，不會消失。
 */
export function groupConversations(
  projects: readonly Project[],
  conversations: readonly Conversation[],
): ConversationGroups {
  const known = new Set(projects.map((p) => p.id));
  const sorted = [...conversations].sort(byUpdatedDesc);
  return {
    projects: [...projects].sort(byUpdatedDesc).map((project) => ({
      project,
      conversations: sorted.filter((c) => c.projectId === project.id),
    })),
    ungrouped: sorted.filter((c) => !c.projectId || !known.has(c.projectId)),
  };
}
