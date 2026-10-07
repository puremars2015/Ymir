import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  ArtifactGroup,
  AdminMakeTopic,
  Attachment,
  AdminOverview,
  AuditLogPage,
  OidcSettings,
  OidcTestResult,
  SaveOidcSettingsRequest,
  TunnelSettings,
  RuntimePolicy,
  SaveRuntimePolicyRequest,
  ExtensionPolicy,
  ExtensionValues,
  MyExtensions,
  OneDriveStatus,
  ConversationOneDrive,
  McpServerAccess,
  PlatformMcpServers,
  SaveMcpServerAccessRequest,
  SaveUserExtensionsRequest,
  UserExtensions,
  AdminUsage,
  ServiceHealth,
  WorkspaceFiles,
  AdminUser,
  ApiProblem,
  CancelExecutionResponse,
  CreateLocalUserRequest,
  LoginProviders,
  MakeTopic,
  ChatMessage,
  Conversation,
  Me,
  ModelOption,
  SendMessageResponse,
  Project,
  RuntimeStatus,
  SaveMakeTopicRequest,
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

  loginProviders(): Observable<LoginProviders> {
    return this.http.get<LoginProviders>('/api/auth/providers');
  }

  passwordLogin(account: string, password: string): Observable<Me> {
    return this.http.post<Me>('/api/auth/password-login', { account, password });
  }

  changePassword(currentPassword: string, newPassword: string): Observable<void> {
    return this.http.post<void>('/api/me/password', { currentPassword, newPassword });
  }

  listMakeTopics(): Observable<MakeTopic[]> {
    return this.http.get<MakeTopic[]>('/api/make-topics');
  }

  adminListMakeTopics(): Observable<AdminMakeTopic[]> {
    return this.http.get<AdminMakeTopic[]>('/api/admin/make-topics');
  }

  adminCreateMakeTopic(request: SaveMakeTopicRequest): Observable<AdminMakeTopic> {
    return this.http.post<AdminMakeTopic>('/api/admin/make-topics', request);
  }

  adminUpdateMakeTopic(topicId: string, request: SaveMakeTopicRequest): Observable<AdminMakeTopic> {
    return this.http.put<AdminMakeTopic>(`/api/admin/make-topics/${topicId}`, request);
  }

  adminDeleteMakeTopic(topicId: string): Observable<void> {
    return this.http.delete<void>(`/api/admin/make-topics/${topicId}`);
  }

  adminOverview(utcOffsetMinutes: number): Observable<AdminOverview> {
    return this.http.get<AdminOverview>('/api/admin/overview', {
      params: { utcOffsetMinutes },
    });
  }

  adminStopRuntime(userId: string): Observable<void> {
    return this.http.post<void>(`/api/admin/runtimes/${userId}/stop`, null);
  }

  adminSearchAudit(params: Record<string, string>): Observable<AuditLogPage> {
    return this.http.get<AuditLogPage>('/api/admin/audit', { params });
  }

  adminGetOidcSettings(): Observable<OidcSettings> {
    return this.http.get<OidcSettings>('/api/admin/settings/oidc');
  }

  adminSaveOidcSettings(request: SaveOidcSettingsRequest): Observable<OidcSettings> {
    return this.http.put<OidcSettings>('/api/admin/settings/oidc', request);
  }

  adminResetOidcSettings(): Observable<OidcSettings> {
    return this.http.delete<OidcSettings>('/api/admin/settings/oidc');
  }

  adminTestOidcSettings(tenantId: string): Observable<OidcTestResult> {
    return this.http.post<OidcTestResult>('/api/admin/settings/oidc/test', { tenantId });
  }

  adminGetTunnelSettings(): Observable<TunnelSettings> {
    return this.http.get<TunnelSettings>('/api/admin/settings/tunnel');
  }

  adminSetTunnelToken(token: string): Observable<TunnelSettings> {
    return this.http.put<TunnelSettings>('/api/admin/settings/tunnel/token', { token });
  }

  adminSetPublicHostname(hostname: string): Observable<TunnelSettings> {
    return this.http.put<TunnelSettings>('/api/admin/settings/tunnel/hostname', { hostname });
  }

  adminGetRuntimePolicy(): Observable<RuntimePolicy> {
    return this.http.get<RuntimePolicy>('/api/admin/settings/runtime');
  }

  adminSaveRuntimePolicy(request: SaveRuntimePolicyRequest): Observable<RuntimePolicy> {
    return this.http.put<RuntimePolicy>('/api/admin/settings/runtime', request);
  }

  adminResetRuntimePolicy(): Observable<RuntimePolicy> {
    return this.http.delete<RuntimePolicy>('/api/admin/settings/runtime');
  }

  /** Agent 擴充能力的全域預設（ADR-0012）。 */
  adminGetExtensionPolicy(): Observable<ExtensionPolicy> {
    return this.http.get<ExtensionPolicy>('/api/admin/settings/extensions');
  }

  adminSaveExtensionPolicy(request: ExtensionValues): Observable<ExtensionPolicy> {
    return this.http.put<ExtensionPolicy>('/api/admin/settings/extensions', request);
  }

  adminGetUserExtensions(userId: string): Observable<UserExtensions> {
    return this.http.get<UserExtensions>(`/api/admin/users/${userId}/extensions`);
  }

  adminSaveUserExtensions(
    userId: string,
    request: SaveUserExtensionsRequest,
  ): Observable<UserExtensions> {
    return this.http.put<UserExtensions>(`/api/admin/users/${userId}/extensions`, request);
  }

  /** OneDrive 連結狀態（ADR-0013）；不含任何 token。 */
  getOneDriveStatus(): Observable<OneDriveStatus> {
    return this.http.get<OneDriveStatus>('/api/connectors/onedrive');
  }

  setOneDriveRoot(path: string): Observable<OneDriveStatus> {
    return this.http.put<OneDriveStatus>('/api/connectors/onedrive/root', { path });
  }

  disconnectOneDrive(): Observable<OneDriveStatus> {
    return this.http.delete<OneDriveStatus>('/api/connectors/onedrive');
  }

  /** 對話（工作目錄）的雲端保存狀態（ADR-0013 §4）。 */
  getConversationOneDrive(conversationId: string): Observable<ConversationOneDrive> {
    return this.http.get<ConversationOneDrive>(`/api/conversations/${conversationId}/onedrive`);
  }

  /** 手動同步 / 重試：排入背景同步。 */
  syncConversationOneDrive(conversationId: string): Observable<ConversationOneDrive> {
    return this.http.post<ConversationOneDrive>(
      `/api/conversations/${conversationId}/onedrive/sync`,
      null,
    );
  }

  /** 目前使用者的擴充能力與自建擴充（只回自己的資料）。 */
  getMyExtensions(): Observable<MyExtensions> {
    return this.http.get<MyExtensions>('/api/extensions');
  }

  adminHealth(): Observable<ServiceHealth> {
    return this.http.get<ServiceHealth>('/api/admin/health');
  }

  adminUsage(days: number): Observable<AdminUsage> {
    return this.http.get<AdminUsage>('/api/admin/usage', { params: { days } });
  }

  listConversationArtifacts(conversationId: string): Observable<ArtifactGroup[]> {
    return this.http.get<ArtifactGroup[]>(`/api/conversations/${conversationId}/artifacts`);
  }

  listConversationFiles(conversationId: string): Observable<WorkspaceFiles> {
    return this.http.get<WorkspaceFiles>(`/api/conversations/${conversationId}/files`);
  }

  /** 檔案預覽：沿用下載端點，以文字或 blob 取回（不在 Ymir 網域上直接開啟檔案）。 */
  fetchFileText(url: string): Observable<string> {
    return this.http.get(url, { responseType: 'text' });
  }

  fetchFileBlob(url: string): Observable<Blob> {
    return this.http.get(url, { responseType: 'blob' });
  }

  /** 平台 MCP 服務與存取清單（ADR-0012 B.4）；不含後端位址。 */
  adminListMcpServers(): Observable<PlatformMcpServers> {
    return this.http.get<PlatformMcpServers>('/api/admin/mcp-servers');
  }

  adminSaveMcpServerAccess(
    name: string,
    request: SaveMcpServerAccessRequest,
  ): Observable<McpServerAccess> {
    return this.http.put<McpServerAccess>(
      `/api/admin/mcp-servers/${encodeURIComponent(name)}/access`,
      request,
    );
  }

  adminListUsers(search: string): Observable<AdminUser[]> {
    const params: Record<string, string> = search.trim() ? { search: search.trim() } : {};
    return this.http.get<AdminUser[]>('/api/admin/users', { params });
  }

  adminCreateLocalUser(request: CreateLocalUserRequest): Observable<AdminUser> {
    return this.http.post<AdminUser>('/api/admin/users', request);
  }

  adminSetUserEnabled(userId: string, enabled: boolean): Observable<AdminUser> {
    return this.http.post<AdminUser>(
      `/api/admin/users/${userId}/${enabled ? 'enable' : 'disable'}`,
      null,
    );
  }

  adminResetPassword(userId: string, newPassword: string): Observable<void> {
    return this.http.post<void>(`/api/admin/users/${userId}/reset-password`, { newPassword });
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

  renameConversation(conversationId: string, title: string): Observable<Conversation> {
    return this.http.patch<Conversation>(`/api/conversations/${conversationId}`, { title });
  }

  /** 「刪除」= 封存：從清單隱藏，訊息與檔案保留。 */
  archiveConversation(conversationId: string): Observable<void> {
    return this.http.delete<void>(`/api/conversations/${conversationId}`);
  }

  archiveProject(projectId: string): Observable<void> {
    return this.http.delete<void>(`/api/projects/${projectId}`);
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
    makeTopicId: string | null = null,
    attachmentIds: string[] = [],
    clientRequestId: string = crypto.randomUUID(),
  ): Observable<SendMessageResponse> {
    return this.http.post<SendMessageResponse>(`/api/conversations/${conversationId}/messages`, {
      content,
      clientRequestId,
      modelId,
      makeTopicId,
      attachmentIds,
    });
  }

  /** 上傳附件：body 直接是檔案內容（不是 multipart），檔名放在 query string；送出訊息時帶回傳的 id。 */
  uploadAttachment(conversationId: string, file: File): Observable<Attachment> {
    return this.http.post<Attachment>(`/api/conversations/${conversationId}/attachments`, file, {
      params: { fileName: file.name },
      headers: { 'Content-Type': 'application/octet-stream' },
    });
  }

  cancelExecution(executionId: string): Observable<CancelExecutionResponse> {
    return this.http.post<CancelExecutionResponse>(`/api/executions/${executionId}/cancel`, null);
  }
}

/** 從 HttpErrorResponse 取出可顯示的錯誤訊息。 */
/** ProblemDetails 的 `code`；不是 API 錯誤時為 null。 */
export function apiErrorCode(error: unknown): string | null {
  return error instanceof HttpErrorResponse
    ? ((error.error as ApiProblem | null)?.code ?? null)
    : null;
}

export function describeApiError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const problem = error.error as ApiProblem | null;
    return problem?.detail ?? `${error.status} ${error.statusText}`;
  }
  return error instanceof Error ? error.message : '發生未知錯誤';
}
