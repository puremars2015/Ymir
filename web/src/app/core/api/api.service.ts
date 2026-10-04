import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  ApiProblem,
  CancelExecutionResponse,
  ChatMessage,
  Conversation,
  Me,
  SendMessageResponse,
  UserRole,
  Workspace,
} from './api-types';

/** 呼叫 Ymir API。認證靠同源 HttpOnly cookie，XSRF header 由 HttpClient 自動加上（ADR-0002）。 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  me(): Observable<Me> {
    return this.http.get<Me>('/api/me');
  }

  devLogin(account: string, displayName: string | null, role: UserRole | null): Observable<Me> {
    return this.http.post<Me>('/api/dev/login', { account, displayName, role });
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', null);
  }

  listWorkspaces(): Observable<Workspace[]> {
    return this.http.get<Workspace[]>('/api/workspaces');
  }

  createWorkspace(name: string): Observable<Workspace> {
    return this.http.post<Workspace>('/api/workspaces', { name });
  }

  getWorkspace(workspaceId: string): Observable<Workspace> {
    return this.http.get<Workspace>(`/api/workspaces/${workspaceId}`);
  }

  listConversations(workspaceId: string): Observable<Conversation[]> {
    return this.http.get<Conversation[]>('/api/conversations', { params: { workspaceId } });
  }

  createConversation(workspaceId: string, title: string): Observable<Conversation> {
    return this.http.post<Conversation>('/api/conversations', { workspaceId, title });
  }

  getConversation(conversationId: string): Observable<Conversation> {
    return this.http.get<Conversation>(`/api/conversations/${conversationId}`);
  }

  listMessages(conversationId: string): Observable<ChatMessage[]> {
    return this.http.get<ChatMessage[]>(`/api/conversations/${conversationId}/messages`);
  }

  /** 每次送出產生新的 clientRequestId；網路重送時後端以它防止重複執行（SA §9.1）。 */
  sendMessage(
    conversationId: string,
    content: string,
    clientRequestId: string = crypto.randomUUID(),
  ): Observable<SendMessageResponse> {
    return this.http.post<SendMessageResponse>(`/api/conversations/${conversationId}/messages`, {
      content,
      clientRequestId,
    });
  }

  cancelExecution(executionId: string): Observable<CancelExecutionResponse> {
    return this.http.post<CancelExecutionResponse>(`/api/executions/${executionId}/cancel`, null);
  }
}

/** 從 HttpErrorResponse 取出可顯示的錯誤訊息。 */
export function describeApiError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const problem = error.error as ApiProblem | null;
    return problem?.detail ?? `${error.status} ${error.statusText}`;
  }
  return error instanceof Error ? error.message : '發生未知錯誤';
}
