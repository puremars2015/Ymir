import { components } from './schema';

/** API 型別一律由 OpenAPI 產生（`npm run api:generate`），不要手寫重複的 DTO。 */
type Schemas = components['schemas'];

export type Me = Schemas['MeResponse'];
export type UserRole = Schemas['UserRole'];
export type Workspace = Schemas['WorkspaceResponse'];
export type RuntimeStatus = Schemas['RuntimeStatusResponse'];
export type Conversation = Schemas['ConversationResponse'];
export type ChatMessage = Schemas['MessageResponse'];
export type SendMessageResponse = Schemas['SendMessageResponse'];
export type CancelExecutionResponse = Schemas['CancelExecutionResponse'];

/** ProblemDetails 的 `code`（SA §13）。 */
export interface ApiProblem {
  status?: number;
  detail?: string;
  code?: string;
}
