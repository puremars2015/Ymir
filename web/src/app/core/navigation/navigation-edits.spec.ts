import { Conversation, Project } from '../api/api-types';
import {
  normalizeTitle,
  renameConversationIn,
  renameProjectIn,
  withoutConversation,
  withoutProject,
} from './navigation-edits';

const conversation = (id: string, projectId: string | null = null): Conversation => ({
  id,
  projectId,
  title: id,
  modelId: null,
  status: 'ACTIVE',
  createdAt: '2026-10-06T00:00:00Z',
  updatedAt: '2026-10-06T00:00:00Z',
  activeExecutionId: null,
});

const project = (id: string): Project =>
  ({ id, name: id, systemPrompt: null, status: 'ACTIVE', createdAt: '', updatedAt: '' }) as Project;

describe('navigation edits', () => {
  it('normalizes titles', () => {
    expect(normalizeTitle('  新標題 ')).toBe('新標題');
    expect(normalizeTitle('   ')).toBeNull();
    expect(normalizeTitle('x'.repeat(301))).toBeNull();
  });

  it('renames only the target', () => {
    const list = [conversation('a'), conversation('b')];
    expect(renameConversationIn(list, 'b', '新').map((c) => c.title)).toEqual(['a', '新']);
    expect(renameProjectIn([project('p')], 'p', '專案').map((p) => p.name)).toEqual(['專案']);
  });

  it('removes a conversation, or a project with its conversations', () => {
    const list = [conversation('a', 'p'), conversation('b'), conversation('c', 'q')];
    expect(withoutConversation(list, 'b').map((c) => c.id)).toEqual(['a', 'c']);
    const result = withoutProject([project('p'), project('q')], list, 'p');
    expect(result.projects.map((p) => p.id)).toEqual(['q']);
    expect(result.conversations.map((c) => c.id)).toEqual(['b', 'c']);
  });
});
