import { Conversation, Project } from '../api/api-types';
import { deriveTitle, groupConversations } from './navigation';

const project = (id: string, updatedAt: string): Project => ({
  id,
  name: id,
  status: 'ACTIVE',
  createdAt: updatedAt,
  updatedAt,
});

const conversation = (id: string, projectId: string | null, updatedAt: string): Conversation => ({
  id,
  projectId,
  title: id,
  status: 'ACTIVE',
  createdAt: updatedAt,
  updatedAt,
});

describe('deriveTitle', () => {
  it('uses the first non-empty line, collapsing whitespace', () => {
    expect(deriveTitle('\n\n  幫我   建立 Todo   \n第二行')).toBe('幫我 建立 Todo');
  });

  it('truncates long text by characters and adds an ellipsis', () => {
    expect(deriveTitle('一二三四五六', 4)).toBe('一二三四…');
    expect(deriveTitle('😀😀😀', 2)).toBe('😀😀…');
  });

  it('falls back to a default title for blank input', () => {
    expect(deriveTitle('   \n ')).toBe('新對話');
  });
});

describe('groupConversations', () => {
  it('groups by project and keeps ungrouped chats separately, newest first', () => {
    const groups = groupConversations(
      [project('p-old', '2026-10-01T00:00:00Z'), project('p-new', '2026-10-03T00:00:00Z')],
      [
        conversation('c1', 'p-old', '2026-10-01T01:00:00Z'),
        conversation('c2', null, '2026-10-02T00:00:00Z'),
        conversation('c3', 'p-old', '2026-10-04T00:00:00Z'),
        conversation('c4', null, '2026-10-05T00:00:00Z'),
      ],
    );

    expect(groups.projects.map((g) => g.project.id)).toEqual(['p-new', 'p-old']);
    expect(groups.projects[1].conversations.map((c) => c.id)).toEqual(['c3', 'c1']);
    expect(groups.projects[0].conversations).toEqual([]);
    expect(groups.ungrouped.map((c) => c.id)).toEqual(['c4', 'c2']);
  });

  it('treats conversations of unknown projects as ungrouped', () => {
    const groups = groupConversations([], [conversation('c1', 'missing', '2026-10-01T00:00:00Z')]);
    expect(groups.ungrouped.map((c) => c.id)).toEqual(['c1']);
  });
});
