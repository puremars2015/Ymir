import { Injectable } from '@angular/core';

export interface PendingPrompt {
  prompt: string;
  modelId: string | null;
  thinkingLevel?: string | null;
  /** 從 `/make` 主題按鈕開始的新對話。 */
  makeTopicId: string | null;
  /** 附加的檔案（對話建立後才能上傳，所以先暫存在記憶體）。 */
  files: File[];
}

/**
 * 「直接開聊」時，新對話建立後才導到對話頁；第一則訊息（與選的模型）先暫存在這裡，由對話頁取出後送出。
 * 只存在記憶體，重新整理就消失（此時對話已建立，使用者可以重新輸入）。
 */
@Injectable({ providedIn: 'root' })
export class PendingPromptService {
  private readonly prompts = new Map<string, PendingPrompt>();

  set(conversationId: string, pending: PendingPrompt): void {
    this.prompts.set(conversationId, pending);
  }

  take(conversationId: string): PendingPrompt | undefined {
    const pending = this.prompts.get(conversationId);
    this.prompts.delete(conversationId);
    return pending;
  }
}
