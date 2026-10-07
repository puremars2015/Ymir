import { inject, Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { Conversation } from '../api/api-types';
import { deriveTitle } from './navigation';
import { NavigationStore } from './navigation.store';
import { PendingPromptService } from './pending-prompt.service';

/** 像 ChatGPT 一樣直接開聊：建立對話（標題取自第一則訊息）→ 暫存第一則訊息 → 導到對話頁由它送出。 */
@Injectable({ providedIn: 'root' })
export class ChatStarter {
  private readonly store = inject(NavigationStore);
  private readonly pending = inject(PendingPromptService);
  private readonly router = inject(Router);

  start(
    projectId: string | null,
    firstMessage: string,
    modelId: string | null,
    makeTopicId: string | null = null,
    files: File[] = [],
  ): Observable<Conversation> {
    return this.store.createConversation(projectId, deriveTitle(firstMessage)).pipe(
      tap((conversation) => {
        this.pending.set(conversation.id, { prompt: firstMessage, modelId, makeTopicId, files });
        void this.router.navigate(['/c', conversation.id]);
      }),
    );
  }
}
