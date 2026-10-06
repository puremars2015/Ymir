import { ChatMessage } from '../api/api-types';
import { resumeTurnFrom } from './resume-turn';

const message = (id: string, role: string, content: string): ChatMessage => ({
  id,
  role,
  messageType: 'TEXT',
  content,
  sequenceNo: Number(id),
  executionId: null,
  createdAt: '2026-10-06T00:00:00Z',
});

describe('resumeTurnFrom', () => {
  const history = [message('1', 'USER', '你好'), message('2', 'ASSISTANT', '嗨')];

  it('沒有執行中的 execution 時不接回', () => {
    expect(resumeTurnFrom(history, null)).toBeNull();
    expect(resumeTurnFrom(history, undefined)).toBeNull();
  });

  it('最後一則使用者訊息改由 live turn 顯示', () => {
    const messages = [...history, message('3', 'USER', '做一個網頁')];
    expect(resumeTurnFrom(messages, 'e1')).toEqual({
      history,
      prompt: '做一個網頁',
      executionId: 'e1',
    });
  });

  it('最後一則不是使用者訊息時保留全部歷史', () => {
    expect(resumeTurnFrom(history, 'e1')).toEqual({ history, prompt: '', executionId: 'e1' });
  });
});
