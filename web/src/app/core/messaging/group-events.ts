/**
 * 群組聊天的 SSE 事件 `group-updated` 的內容（ADR-0026）。只出現在 `/api/direct-messages/events` 串流中、
 * 不是任何端點的回應，所以不在 OpenAPI 裡；與後端 `GroupUpdatedResponse`（DirectMessageEndpoints.cs）必須同步修改。
 */
export interface GroupUpdated {
  groupId: string;
  /** 自己被移除或群組被封存：從清單拿掉。 */
  removed: boolean;
}
