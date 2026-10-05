import { Injectable } from '@angular/core';

/**
 * 「直接開聊」時，新對話建立後才導到對話頁；第一則訊息先暫存在這裡，由對話頁取出後送出。
 * 只存在記憶體，重新整理就消失（此時對話已建立，使用者可以重新輸入）。
 */
@Injectable({ providedIn: 'root' })
export class PendingPromptService {
  private readonly prompts = new Map<string, string>();

  set(conversationId: string, prompt: string): void {
    this.prompts.set(conversationId, prompt);
  }

  take(conversationId: string): string | undefined {
    const prompt = this.prompts.get(conversationId);
    this.prompts.delete(conversationId);
    return prompt;
  }
}
