import { Conversation, Project } from '../api/api-types';

/** 側邊欄的樂觀更新：先改畫面，API 失敗時用前一份清單還原。 */
export const MAX_TITLE_LENGTH = 300;

export function normalizeTitle(title: string): string | null {
  const value = title.trim();
  return value && value.length <= MAX_TITLE_LENGTH ? value : null;
}

export const renameConversationIn = (
  list: Conversation[],
  id: string,
  title: string,
): Conversation[] => list.map((c) => (c.id === id ? { ...c, title } : c));

export const renameProjectIn = (list: Project[], id: string, name: string): Project[] =>
  list.map((p) => (p.id === id ? { ...p, name } : p));

export const withoutConversation = (list: Conversation[], id: string): Conversation[] =>
  list.filter((c) => c.id !== id);

/** 封存專案時，專案內的對話也一起從側邊欄移除。 */
export function withoutProject(
  projects: Project[],
  conversations: Conversation[],
  projectId: string,
): { projects: Project[]; conversations: Conversation[] } {
  return {
    projects: projects.filter((p) => p.id !== projectId),
    conversations: conversations.filter((c) => c.projectId !== projectId),
  };
}
