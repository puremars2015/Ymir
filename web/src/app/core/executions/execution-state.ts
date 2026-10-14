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
  /** 工具開始後為 true，直到 Agent 輸出下一段文字或執行終止；用來跨過工具之間的空檔，避免提示閃爍。 */
  toolBusy: boolean;
  error: { code: string; message: string } | null;
}

export const initialExecutionView = (): ExecutionView => ({
  executionId: null,
  status: 'running',
  text: '',
  tools: [],
  statusText: null,
  toolBusy: false,
  error: null,
});

/**
 * 多個工具重疊時仍只顯示一個工作提示；終止事件優先，不保留未結束工具的提示。
 * 連續呼叫工具時，上一個結束到下一個開始之間（模型思考中）也算工作中，提示才不會閃爍；
 * Agent 開始輸出文字或執行終止時才消失。
 */
export function hasRunningTools(view: ExecutionView): boolean {
  return view.status === 'running' && view.toolBusy;
}

export const WORKING_LABEL = '正在工作中......';
export const THINKING_LABEL = '正在思考中......';

/**
 * 執行中的文字提示：工具執行中（含工具之間的空檔）顯示「工作中」；尚未有可顯示的回答時顯示「思考中」。
 * 只要還在執行且畫面上沒有回答，就一定有文字提示，使用者不會只看到空白或沒有文字的動畫
 * （例如模型輸出仍在 <think> 區塊內、被顯示過濾掉時）。執行結束後不顯示。
 */
export function liveIndicator(view: ExecutionView, hasVisibleText: boolean): string | null {
  if (view.status !== 'running') {
    return null;
  }
  if (hasRunningTools(view)) {
    return WORKING_LABEL;
  }
  return hasVisibleText ? null : THINKING_LABEL;
}

/** 純函式：套用一個事件並回傳新的狀態（不修改輸入）。 */
export function applyExecutionEvent(view: ExecutionView, event: ExecutionEvent): ExecutionView {
  switch (event.type) {
    case 'execution.started':
      return { ...view, executionId: event.data.executionId };
    case 'assistant.delta':
      return {
        ...view,
        text: view.text + event.data.text,
        statusText: null,
        toolBusy: view.tools.some((tool) => tool.state === 'running'),
      };
    case 'tool.started':
      return {
        ...view,
        toolBusy: true,
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
      return { ...view, status: 'completed', statusText: null, toolBusy: false };
    case 'execution.failed':
      return { ...view, status: 'failed', statusText: null, toolBusy: false, error: event.data };
    case 'execution.cancelled':
      return { ...view, status: 'cancelled', statusText: null, toolBusy: false };
  }
}
