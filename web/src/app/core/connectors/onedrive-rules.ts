import { ConversationOneDrive, OneDriveStatus } from '../api/api-types';

/** 連結 OneDrive 的整頁導向（瀏覽器導到 Microsoft 再回來；不經 HttpClient，前端不接觸任何 token）。 */
export const ONEDRIVE_CONNECT_URL = '/api/connectors/onedrive/connect?returnUrl=%2Fsettings';

/** callback 導回時的 `?onedrive=` 結果代碼 → 給使用者看的訊息。 */
export function onedriveResultMessage(
  code: string | null,
): { text: string; error: boolean } | null {
  switch (code) {
    case 'connected':
      return { text: '已連結 OneDrive。請設定要同步的資料夾。', error: false };
    case 'denied':
      return { text: '已取消連結 OneDrive。', error: true };
    case 'mismatch':
      return { text: '請使用與 Ymir 相同的公司帳號連結 OneDrive。', error: true };
    case 'forbidden':
      return { text: '管理員尚未開放 OneDrive 連結。', error: true };
    case 'invalid':
      return { text: '連結逾時或已失效，請重新操作。', error: true };
    case 'error':
      return {
        text: '無法連結 OneDrive，請稍後再試；若一直失敗，可能是公司尚未允許 Ymir 存取 OneDrive。',
        error: true,
      };
    default:
      return null;
  }
}

export function onedriveStateLabel(status: OneDriveStatus): string {
  switch (status.state) {
    case 'Connected':
      return status.rootPath ? `已連結，同步到 ${status.rootPath}` : '已連結，尚未設定同步資料夾';
    case 'NeedsReauth':
      return '授權已失效，請重新連結';
    default:
      return '尚未連結';
  }
}

export type OneDriveSyncTone = 'ok' | 'pending' | 'error' | 'hint';

export interface OneDriveSyncSummary {
  text: string;
  tone: OneDriveSyncTone;
  /** 可以手動同步 / 重試。 */
  canSync: boolean;
  /** 需要到設定頁處理（連結、重新連結、選資料夾）。 */
  needsSettings: boolean;
  /** 同步中：前端定期重新讀取狀態。 */
  pending: boolean;
}

/**
 * 檔案面板的「雲端保存狀態」（ADR-0013 §4）。與 Agent 任務結果分開：同步失敗不代表任務失敗。
 * 管理員沒有開放時回傳 null（不顯示）。
 */
export function onedriveSyncSummary(
  status: ConversationOneDrive | null,
  formatTime: (iso: string) => string,
): OneDriveSyncSummary | null {
  if (!status || status.availability === 'NotAllowed') {
    return null;
  }

  const hint = (text: string): OneDriveSyncSummary => ({
    text,
    tone: 'hint',
    canSync: false,
    needsSettings: true,
    pending: false,
  });
  switch (status.availability) {
    case 'NotConnected':
      return hint('尚未連結 OneDrive，檔案只保存在 Ymir。');
    case 'NeedsReauth':
      return hint('OneDrive 授權已失效，請重新連結；檔案仍保存在 Ymir。');
    case 'NoRoot':
      return hint('已連結 OneDrive，請先選擇同步資料夾。');
  }

  // OpenAPI 把 int 產生成 number | string。
  const conflictCount = Number(status.conflictCount);
  const conflicts = conflictCount > 0 ? `；已另存 ${conflictCount} 個衝突副本` : '';
  switch (status.state) {
    case 'Pending':
      return {
        text: '正在同步到 OneDrive…',
        tone: 'pending',
        canSync: false,
        needsSettings: false,
        pending: true,
      };
    case 'Failed':
      return {
        text: `OneDrive 同步失敗：${status.lastError ?? '請稍後再試。'}`,
        tone: 'error',
        canSync: true,
        needsSettings: false,
        pending: false,
      };
    case 'Synced':
      if (status.lastSyncedAt) {
        const note = status.lastError ? `（${status.lastError}）` : '';
        return {
          text: `已同步到 OneDrive · ${formatTime(status.lastSyncedAt)}${conflicts}${note}`,
          tone: 'ok',
          canSync: true,
          needsSettings: false,
          pending: false,
        };
      }
      break;
  }

  return {
    text: '執行 Agent 後會自動同步到 OneDrive。',
    tone: 'hint',
    canSync: true,
    needsSettings: false,
    pending: false,
  };
}

/** 雲端資料夾的完整位置，例如 /Ymir/chats/報告-1a2b3c4d。 */
export function onedriveFolderLabel(status: ConversationOneDrive | null): string | null {
  return status?.rootPath && status.folderPath ? `${status.rootPath}/${status.folderPath}` : null;
}
