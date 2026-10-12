/** Ymir 是開源專案名稱，固定不變；公司名稱由管理者設定（空白表示不顯示）。 */
export const PROJECT_NAME = 'Ymir';

/** 畫面上的平台名稱：「{公司名稱} Ymir」，沒有公司名稱就只有 Ymir。 */
export function brandName(companyName: string | null | undefined): string {
  const company = companyName?.trim();
  return company ? `${company} ${PROJECT_NAME}` : PROJECT_NAME;
}

export const MAX_COMPANY_NAME_LENGTH = 40;
export const MAX_FAVICON_BYTES = 256 * 1024;

export function companyNameProblem(value: string): string | null {
  const text = value.trim();
  if (text.length > MAX_COMPANY_NAME_LENGTH) {
    return `公司名稱不超過 ${MAX_COMPANY_NAME_LENGTH} 個字。`;
  }
  // eslint-disable-next-line no-control-regex
  return /[\u0000-\u001f\u007f]/.test(text) ? '公司名稱不可包含控制字元。' : null;
}

/** 與後端相同的檔頭判斷（PNG / ICO / JPEG / WebP），不收 SVG。 */
export function faviconProblem(size: number, header: Uint8Array): string | null {
  if (size > MAX_FAVICON_BYTES) {
    return `圖示太大（上限 ${MAX_FAVICON_BYTES / 1024} KB）。`;
  }
  const startsWith = (...bytes: number[]) => bytes.every((b, i) => header[i] === b);
  const supported =
    startsWith(0x89, 0x50, 0x4e, 0x47) ||
    startsWith(0x00, 0x00, 0x01, 0x00) ||
    startsWith(0xff, 0xd8, 0xff) ||
    (startsWith(0x52, 0x49, 0x46, 0x46) &&
      header[8] === 0x57 &&
      header[9] === 0x45 &&
      header[10] === 0x42 &&
      header[11] === 0x50);
  return supported ? null : '圖示格式不支援，請使用 PNG、ICO、JPEG 或 WebP。';
}
