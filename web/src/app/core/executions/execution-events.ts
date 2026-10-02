/**
 * SSE 事件契約（SA §10）。對應後端 `Ymir.VibeMaker.Contracts.Executions`。
 * Sprint 1 起改由 OpenAPI 產生，請勿在其他地方重複定義。
 */
export type ExecutionEvent =
  | { type: 'execution.started'; data: { executionId: string } }
  | { type: 'assistant.delta'; data: { text: string } }
  | { type: 'tool.started'; data: { tool: string; callId: string; summary: string } }
  | { type: 'tool.completed'; data: { callId: string; success: boolean } }
  | { type: 'status'; data: { text: string } }
  | { type: 'execution.completed'; data: { executionId: string; messageId: string | null } }
  | { type: 'execution.failed'; data: { code: string; message: string } }
  | { type: 'execution.cancelled'; data: { executionId: string } };

export type ExecutionEventType = ExecutionEvent['type'];

export const EXECUTION_EVENT_TYPES: readonly ExecutionEventType[] = [
  'execution.started',
  'assistant.delta',
  'tool.started',
  'tool.completed',
  'status',
  'execution.completed',
  'execution.failed',
  'execution.cancelled',
];

export const TERMINAL_EVENT_TYPES: ReadonlySet<ExecutionEventType> = new Set([
  'execution.completed',
  'execution.failed',
  'execution.cancelled',
]);
