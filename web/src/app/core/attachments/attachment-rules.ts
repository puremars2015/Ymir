/**
 * 訊息附件的前端規則（上限與後端 `AttachmentRules` 一致；後端仍會再檢查一次）。
 * 檔案在按下送出時才上傳到對話的工作目錄 `uploads/`，所以新對話也能附加檔案。
 */
export const MAX_ATTACHMENT_BYTES = 50 * 1024 * 1024;
export const MAX_ATTACHMENTS = 10;

/** 只附檔案、沒有輸入文字時送給 Agent 的內容。 */
export const DEFAULT_ATTACHMENT_PROMPT = '請看看我附加的檔案。';

export interface AttachmentSelection {
  files: File[];
  /** 被略過的檔案與原因，直接顯示給使用者。 */
  rejected: string[];
}

const sameFile = (a: File, b: File): boolean =>
  a.name === b.name && a.size === b.size && a.lastModified === b.lastModified;

/** 加入新選的檔案：略過重複、空檔、太大的檔案，並限制總數。 */
export function addAttachments(
  current: readonly File[],
  incoming: readonly File[],
): AttachmentSelection {
  const files = [...current];
  const rejected: string[] = [];
  for (const file of incoming) {
    if (files.some((f) => sameFile(f, file))) {
      continue;
    }
    if (file.size === 0) {
      rejected.push(`${file.name}：空檔案`);
    } else if (file.size > MAX_ATTACHMENT_BYTES) {
      rejected.push(`${file.name}：超過 ${MAX_ATTACHMENT_BYTES / 1024 / 1024} MB`);
    } else if (files.length >= MAX_ATTACHMENTS) {
      rejected.push(`${file.name}：一次最多 ${MAX_ATTACHMENTS} 個檔案`);
    } else {
      files.push(file);
    }
  }
  return { files, rejected };
}

/** 送出的文字：沒有輸入但有附件時用預設文字（後端要求訊息內容不得為空）。 */
export function submissionContent(text: string, files: readonly File[]): string {
  const trimmed = text.trim();
  return trimmed || (files.length > 0 ? DEFAULT_ATTACHMENT_PROMPT : '');
}

/** 後端以檔頭判斷的類型（`image/png` 等）；SVG 也可以用 `<img>` 安全顯示（不執行腳本）。 */
export const isImageType = (contentType: string | null | undefined): boolean =>
  !!contentType && contentType.startsWith('image/');

export function attachmentIcon(contentType: string): string {
  if (isImageType(contentType)) return '🖼';
  if (contentType.startsWith('video/')) return '🎬';
  if (contentType.startsWith('audio/')) return '🎵';
  if (contentType === 'application/pdf') return '📕';
  if (contentType.startsWith('text/')) return '📝';
  return '📎';
}
