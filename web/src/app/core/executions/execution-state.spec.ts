import { ExecutionEvent } from './execution-events';
import { applyExecutionEvent, initialExecutionView } from './execution-state';

const run = (events: ExecutionEvent[]) =>
  events.reduce(applyExecutionEvent, initialExecutionView());

describe('applyExecutionEvent', () => {
  it('accumulates text and tracks tool calls until completion', () => {
    const view = run([
      { type: 'execution.started', data: { executionId: 'e1' } },
      { type: 'status', data: { text: '正在準備 Runtime' } },
      { type: 'assistant.delta', data: { text: '我來建立檔案。' } },
      { type: 'tool.started', data: { tool: 'bash', callId: 'c1', summary: 'npm install' } },
      { type: 'tool.completed', data: { callId: 'c1', success: true } },
      { type: 'assistant.delta', data: { text: '\n\n完成' } },
      { type: 'execution.completed', data: { executionId: 'e1', messageId: null } },
    ]);

    expect(view.executionId).toBe('e1');
    expect(view.status).toBe('completed');
    expect(view.text).toBe('我來建立檔案。\n\n完成');
    expect(view.tools).toEqual([
      { tool: 'bash', callId: 'c1', summary: 'npm install', state: 'succeeded' },
    ]);
    expect(view.statusText).toBeNull();
  });

  it('records failures without losing partial text', () => {
    const view = run([
      { type: 'assistant.delta', data: { text: 'partial' } },
      {
        type: 'execution.failed',
        data: { code: 'MODEL_PROVIDER_ERROR', message: '模型服務發生錯誤' },
      },
    ]);

    expect(view.status).toBe('failed');
    expect(view.text).toBe('partial');
    expect(view.error).toEqual({ code: 'MODEL_PROVIDER_ERROR', message: '模型服務發生錯誤' });
  });

  it('marks failed tools', () => {
    const view = run([
      { type: 'tool.started', data: { tool: 'bash', callId: 'c1', summary: 'exit 1' } },
      { type: 'tool.completed', data: { callId: 'c1', success: false } },
      { type: 'execution.cancelled', data: { executionId: 'e1' } },
    ]);

    expect(view.tools[0].state).toBe('failed');
    expect(view.status).toBe('cancelled');
  });
});
