import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  ApiProblem,
  CancelExecutionResponse,
  ChatMessage,
  Conversation,
  Me,
  ModelOption,
  SendMessageResponse,
  Project,
  RuntimeStatus,
  UserRole,
  UserSettings,
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

  listProjects(): Observable<Project[]> {
    return this.http.get<Project[]>('/api/projects');
  }

  createProject(name: string): Observable<Project> {
    return this.http.post<Project>('/api/projects', { name });
  }

  getProject(projectId: string): Observable<Project> {
    return this.http.get<Project>(`/api/projects/${projectId}`);
  }

  /** 只送有變更的欄位；systemPrompt 傳空字串表示清除。 */
  updateProject(
    projectId: string,
    changes: { name?: string; systemPrompt?: string },
  ): Observable<Project> {
    return this.http.patch<Project>(`/api/projects/${projectId}`, {
      name: changes.name ?? null,
      systemPrompt: changes.systemPrompt ?? null,
    });
  }

  listModels(): Observable<ModelOption[]> {
    return this.http.get<ModelOption[]>('/api/models');
  }

  getSettings(): Observable<UserSettings> {
    return this.http.get<UserSettings>('/api/me/settings');
  }

  updateSettings(systemPrompt: string): Observable<UserSettings> {
    return this.http.put<UserSettings>('/api/me/settings', { systemPrompt });
  }

  /** 目前使用者的執行環境（一個使用者一個 container，ADR-0007）。 */
  getRuntime(): Observable<RuntimeStatus> {
    return this.http.get<RuntimeStatus>('/api/runtime');
  }

  /** 不帶 projectId 時回傳全部對話（含未分組），側邊欄依 projectId 分組。 */
  listConversations(projectId?: string): Observable<Conversation[]> {
    return this.http.get<Conversation[]>('/api/conversations', {
      params: projectId ? { projectId } : {},
    });
  }

  /** projectId 為 null 時建立未分組的對話。 */
  createConversation(projectId: string | null, title: string): Observable<Conversation> {
    return this.http.post<Conversation>('/api/conversations', { projectId, title });
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
    modelId: string | null = null,
    clientRequestId: string = crypto.randomUUID(),
  ): Observable<SendMessageResponse> {
    return this.http.post<SendMessageResponse>(`/api/conversations/${conversationId}/messages`, {
      content,
      clientRequestId,
      modelId,
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
