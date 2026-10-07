import { describe, expect, it } from 'vitest';
import { ArtifactGroup } from '../api/api-types';
import { artifactDelivery } from './artifact-files';

const group = (paths: string[]): ArtifactGroup => ({
  executionId: 'run',
  conversationId: 'chat',
  messageId: null,
  createdAt: '2026-10-07T00:00:00Z',
  files: paths.map((path) => ({ path, size: 3, modifiedAt: '2026-10-07T00:00:00Z' })),
});
describe('artifact delivery', () => {
  it('does not recommend a download for an analysis-only response', () =>
    expect(artifactDelivery(group([]), 'chat')).toBeNull());
  it('downloads a single deliverable directly with an encoded filename', () =>
    expect(artifactDelivery(group(['中文 摘要.txt']), 'chat')?.url).toBe(
      '/api/conversations/chat/artifacts/run/download?path=' + encodeURIComponent('中文 摘要.txt'),
    ));
  it('offers one ZIP for a website including its legitimate package configuration', () => {
    const delivery = artifactDelivery(group(['index.html', 'package.json']), 'chat');
    expect(delivery?.url).toBe('/api/conversations/chat/artifacts/run/archive');
    expect(delivery?.label).toContain('2 個檔案');
  });
});
