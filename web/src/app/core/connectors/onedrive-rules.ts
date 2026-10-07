import { OneDriveStatus } from '../api/api-types';

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
