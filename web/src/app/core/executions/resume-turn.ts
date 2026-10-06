import { ChatMessage } from '../api/api-types';

export interface ResumedTurn {
  /** 已完成的歷史訊息（不含執行中這一輪的使用者訊息）。 */
  history: ChatMessage[];
  /** 執行中這一輪的使用者訊息；改由 live turn 顯示，避免重複。 */
  prompt: string;
  executionId: string;
}

/**
 * 重新整理或切回對話時，若後端回報有執行中的 execution，接回它的 SSE（從頭重播事件）。
 * Agent 的回覆在執行結束才寫入訊息，所以執行中最後一則一定是這一輪的使用者訊息。
 */
export function resumeTurnFrom(
  messages: ChatMessage[],
  activeExecutionId: string | null | undefined,
): ResumedTurn | null {
  if (!activeExecutionId) {
    return null;
  }
  const last = messages.at(-1);
  if (last?.role === 'USER') {
    return { history: messages.slice(0, -1), prompt: last.content, executionId: activeExecutionId };
  }
  return { history: messages, prompt: '', executionId: activeExecutionId };
}
