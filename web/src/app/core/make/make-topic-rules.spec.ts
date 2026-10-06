import { AdminMakeTopic } from '../api/api-types';
import {
  draftFromTopic,
  emptyMakeTopicDraft,
  makeTopicDraftProblem,
  moveTopic,
  nextSortOrder,
  sortTopics,
  toSaveRequest,
} from './make-topic-rules';

const topic = (id: string, sortOrder: number | string, name = id): AdminMakeTopic => ({
  id,
  name,
  description: null,
  instructions: '指示',
  sortOrder,
  isEnabled: true,
  updatedAt: '2026-10-06T00:00:00Z',
});

describe('makeTopicDraftProblem', () => {
  it('requires name and instructions', () => {
    expect(makeTopicDraftProblem(emptyMakeTopicDraft())).toBe('請輸入主題名稱');
    expect(makeTopicDraftProblem({ ...emptyMakeTopicDraft(), name: '工具' })).toBe(
      '請輸入給 Agent 的建置指示',
    );
    expect(
      makeTopicDraftProblem({ ...emptyMakeTopicDraft(), name: ' 工具 ', instructions: '做' }),
    ).toBeNull();
  });

  it('enforces the server-side length limits', () => {
    const base = { ...emptyMakeTopicDraft(), name: 'a', instructions: 'b' };
    expect(makeTopicDraftProblem({ ...base, name: 'x'.repeat(51) })).toContain('50');
    expect(makeTopicDraftProblem({ ...base, description: 'x'.repeat(201) })).toContain('200');
    expect(makeTopicDraftProblem({ ...base, instructions: 'x'.repeat(4001) })).toContain('4000');
  });
});

describe('toSaveRequest / draftFromTopic', () => {
  it('trims text and turns an empty description into null', () => {
    expect(
      toSaveRequest(
        { name: ' 工具 ', description: '  ', instructions: ' 做 ', isEnabled: false },
        30,
      ),
    ).toEqual({
      name: '工具',
      description: null,
      instructions: '做',
      sortOrder: 30,
      isEnabled: false,
    });
  });

  it('round-trips an existing topic', () => {
    expect(draftFromTopic({ ...topic('a', 10), description: '說明' })).toEqual({
      name: 'a',
      description: '說明',
      instructions: '指示',
      isEnabled: true,
    });
  });
});

describe('sorting', () => {
  it('sorts by numeric sort order, then name', () => {
    const sorted = sortTopics([topic('b', '20'), topic('c', 5), topic('a', 20)]);
    expect(sorted.map((t) => t.id)).toEqual(['c', 'a', 'b']);
  });

  it('puts a new topic after the current maximum', () => {
    expect(nextSortOrder([])).toBe(10);
    expect(nextSortOrder([topic('a', 10), topic('b', '25')])).toBe(35);
  });
});

describe('moveTopic', () => {
  const topics = [topic('a', 10), topic('b', 20), topic('c', 30)];

  it('swaps sort orders with the neighbour', () => {
    expect(moveTopic(topics, 1, -1)).toEqual([
      { id: 'b', sortOrder: 10 },
      { id: 'a', sortOrder: 20 },
    ]);
    expect(moveTopic(topics, 1, 1)).toEqual([
      { id: 'b', sortOrder: 30 },
      { id: 'c', sortOrder: 20 },
    ]);
  });

  it('does nothing at the edges', () => {
    expect(moveTopic(topics, 0, -1)).toEqual([]);
    expect(moveTopic(topics, 2, 1)).toEqual([]);
  });

  it('renumbers when neighbours share the same sort order', () => {
    const tied = [topic('a', 10), topic('b', 10), topic('c', 10)];
    expect(moveTopic(tied, 1, -1)).toEqual([
      { id: 'a', sortOrder: 20 },
      { id: 'c', sortOrder: 30 },
    ]);
  });
});
