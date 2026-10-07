import { components } from './schema';

/** API 型別一律由 OpenAPI 產生（`npm run api:generate`），不要手寫重複的 DTO。 */
type Schemas = components['schemas'];

export type Me = Schemas['MeResponse'];
export type UserRole = Schemas['UserRole'];
export type Project = Schemas['ProjectResponse'];
export type RuntimeStatus = Schemas['RuntimeStatusResponse'];
export type Conversation = Schemas['ConversationResponse'];
export type ChatMessage = Schemas['MessageResponse'];
export type SendMessageResponse = Schemas['SendMessageResponse'];
export type CancelExecutionResponse = Schemas['CancelExecutionResponse'];
export type ModelOption = Schemas['ModelResponse'];
export type UserSettings = Schemas['UserSettingsResponse'];
export type LoginProviders = Schemas['LoginProvidersResponse'];
export type AuthMethod = Schemas['AuthMethod'];
export type AdminUser = Schemas['AdminUserResponse'];
export type UserStatus = Schemas['UserStatus'];
export type CreateLocalUserRequest = Schemas['CreateLocalUserRequest'];
export type MakeTopic = Schemas['MakeTopicResponse'];
export type AdminMakeTopic = Schemas['AdminMakeTopicResponse'];
export type SaveMakeTopicRequest = Schemas['SaveMakeTopicRequest'];
export type AdminOverview = Schemas['AdminOverviewResponse'];
export type RuntimeSummary = Schemas['RuntimeSummaryResponse'];
export type DailyExecution = Schemas['DailyExecutionResponse'];
export type AuditLogPage = Schemas['AuditLogPageResponse'];
export type AuditLogItem = Schemas['AuditLogItemResponse'];
export type AuditResult = Schemas['AuditResult'];
export type RuntimeState = Schemas['RuntimeStatus'];
export type OidcSettings = Schemas['OidcSettingsResponse'];
export type OidcSettingsSource = Schemas['OidcSettingsSource'];
export type SaveOidcSettingsRequest = Schemas['SaveOidcSettingsRequest'];
export type OidcTestResult = Schemas['OidcTestResponse'];
export type WorkspaceFile = Schemas['WorkspaceFileResponse'];
export type TunnelSettings = Schemas['TunnelSettingsResponse'];
export type WorkspaceFiles = Schemas['WorkspaceFilesResponse'];
export type RuntimePolicy = Schemas['RuntimePolicyResponse'];
export type RuntimePolicyValues = Schemas['RuntimePolicyValues'];
export type SaveRuntimePolicyRequest = Schemas['SaveRuntimePolicyRequest'];
export type AdminUsage = Schemas['AdminUsageResponse'];
export type ServiceHealth = Schemas['ServiceHealthResponse'];
export type ServiceHealthItem = Schemas['ServiceHealthItem'];
export type UserUsage = Schemas['UserUsageResponse'];
export type ExtensionPolicy = Schemas['ExtensionPolicyResponse'];
export type ExtensionValues = Schemas['ExtensionValues'];
export type ExtensionGrantSetting = Schemas['ExtensionGrantSetting'];
export type UserExtensions = Schemas['UserExtensionsResponse'];
export type SaveUserExtensionsRequest = Schemas['SaveUserExtensionsRequest'];
export type MyExtensions = Schemas['MyExtensionsResponse'];
export type RestrictedNetworkSupport = Schemas['RestrictedNetworkSupport'];

/** ProblemDetails 的 `code`（SA §13）。 */
export interface ApiProblem {
  status?: number;
  detail?: string;
  code?: string;
}
