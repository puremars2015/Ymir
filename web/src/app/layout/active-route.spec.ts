import { parseActiveRoute } from './active-route';

describe('parseActiveRoute', () => {
  it('reads conversation and project ids from the url', () => {
    expect(parseActiveRoute('/c/abc?x=1')).toEqual({ conversationId: 'abc', projectId: null });
    expect(parseActiveRoute('/projects/p1')).toEqual({ conversationId: null, projectId: 'p1' });
    expect(parseActiveRoute('/')).toEqual({ conversationId: null, projectId: null });
  });
});
