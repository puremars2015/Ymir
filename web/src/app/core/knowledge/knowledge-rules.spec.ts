import { KnowledgeDocument } from '../api/api-types';
import {
  hasPendingDocuments,
  knowledgeStatusLabel,
  validateKnowledgeFile,
} from './knowledge-rules';

const doc = (status: KnowledgeDocument['status']): KnowledgeDocument => ({
  id: '1',
  fileName: 'a.txt',
  version: 1,
  size: 1,
  status,
  chunkCount: 0,
  error: null,
  createdAt: '2026-10-08T00:00:00Z',
  updatedAt: '2026-10-08T00:00:00Z',
});

describe('knowledge rules', () => {
  it('labels statuses', () => {
    expect(knowledgeStatusLabel('Ready')).toBe('可查詢');
    expect(knowledgeStatusLabel('Failed')).toBe('失敗');
    expect(knowledgeStatusLabel('Indexing')).toBe('建立索引中');
  });

  it('detects pending documents', () => {
    expect(hasPendingDocuments([doc('Ready'), doc('Pending')])).toBe(true);
    expect(hasPendingDocuments([doc('Ready'), doc('Failed')])).toBe(false);
  });

  it('validates extension and size', () => {
    const exts = ['.txt', '.pdf'];
    expect(validateKnowledgeFile({ name: '報告.PDF', size: 10 }, exts, 100)).toBeNull();
    expect(validateKnowledgeFile({ name: 'a.exe', size: 10 }, exts, 100)).toContain('格式不支援');
    expect(
      validateKnowledgeFile({ name: 'a.txt', size: 2 * 1024 * 1024 }, exts, 1024 * 1024),
    ).toContain('超過 1 MB');
  });
});
