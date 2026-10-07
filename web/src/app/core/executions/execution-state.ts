import { ExecutionEvent } from './execution-events';

export type ExecutionStatus = 'running' | 'completed' | 'failed' | 'cancelled';

export interface ToolCallView {
  callId: string;
  tool: string;
  summary: string;
  state: 'running' | 'succeeded' | 'failed';
}

/** 一次 Agent 執行在畫面上的狀態。由 {@link applyExecutionEvent} 依序套用 SSE 事件產生。 */
export interface ExecutionView {
  executionId: string | null;
  status: ExecutionStatus;
  text: string;
  tools: ToolCallView[];
  statusText: string | null;
  error: { code: string; message: string } | null;
}

export const initialExecutionView = (): ExecutionView => ({
  executionId: null,
  status: 'running',
  text: '',
  tools: [],
  statusText: null,
  error: null,
});

/** 多個工具重疊時仍只顯示一個工作提示；終止事件優先，不保留未結束工具的提示。 */
export function hasRunningTools(view: ExecutionView): boolean {
  return view.status === 'running' && view.tools.some((tool) => tool.state === 'running');
}

/** 純函式：套用一個事件並回傳新的狀態（不修改輸入）。 */
export function applyExecutionEvent(view: ExecutionView, event: ExecutionEvent): ExecutionView {
  switch (event.type) {
    case 'execution.started':
      return { ...view, executionId: event.data.executionId };
    case 'assistant.delta':
      return { ...view, text: view.text + event.data.text, statusText: null };
    case 'tool.started':
      return {
        ...view,
        tools: [...view.tools, { ...event.data, state: 'running' }],
      };
    case 'tool.completed':
      return {
        ...view,
        tools: view.tools.map((tool) =>
          tool.callId === event.data.callId
            ? { ...tool, state: event.data.success ? 'succeeded' : 'failed' }
            : tool,
        ),
      };
    case 'status':
      return { ...view, statusText: event.data.text };
    case 'execution.completed':
      return { ...view, status: 'completed', statusText: null };
    case 'execution.failed':
      return { ...view, status: 'failed', statusText: null, error: event.data };
    case 'execution.cancelled':
      return { ...view, status: 'cancelled', statusText: null };
  }
}
